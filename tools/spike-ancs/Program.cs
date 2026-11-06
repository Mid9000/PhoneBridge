using PhoneBridge.Bluetooth;
using PhoneBridge.Bluetooth.Ancs;
using PhoneBridge.Bluetooth.Platform;
using PhoneBridge.Spikes;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

// SPIKE ANCS (SPEC §5). Find the iPhone over LE, find ANCS, subscribe, print notifications (content masked unless --verbose).
if (Args.Has(args, "--help") || Args.Has(args, "-h"))
{
    Console.WriteLine("""
        spike-ancs [--device <name part>] [--duration 120] [--pair] [--no-advert] [--peripheral] [--verbose]
          --device      pick the paired device whose name contains this (default: first "iPhone")
          --duration    seconds to look for ANCS and then listen for notifications
          --pair        if the iPhone is reachable over LE but not LE-bonded, pair it from here (confirm the code)
          --no-advert   do not broadcast the ANCS solicitation advert
          --peripheral  also broadcast a connectable advert (PC as BLE peripheral)
          --verbose     show notification titles and text (the log file then contains private data)
        """);
    return 0;
}

using var log = new SpikeLog("spike-ancs", Args.Has(args, "--verbose"));
int duration = Args.Int(args, "--duration", 120);

log.Step("Bluetooth adapter");
var adapter = await WindowsBluetooth.GetAdapterAsync();
if (adapter is null)
{
    log.Fail("No Bluetooth adapter found (or Bluetooth is off).");
    log.Hint("Paramètres Windows > Bluetooth et appareils : activer le Bluetooth.");
    log.Result("Adapter", "FAIL none");
    log.PrintSummary();
    return 2;
}
log.Info($"Address {adapter.Address}, radio {adapter.RadioState}, Classic={adapter.ClassicSupported}, LE={adapter.LowEnergySupported}, " +
         $"central={adapter.CentralRoleSupported}, peripheral={adapter.PeripheralRoleSupported}, offload={adapter.AdvertisementOffloadSupported}, maxAdv={adapter.MaxAdvertisementDataLength}");
log.Result("Adapter", adapter.LowEnergySupported ? $"OK {adapter.Address} radio={adapter.RadioState} peripheral={adapter.PeripheralRoleSupported}" : "FAIL no LE");
if (!adapter.LowEnergySupported) { log.PrintSummary(); return 2; }

AncsAdvertiser? advertiser = null;
if (!Args.Has(args, "--no-advert"))
{
    log.Step("BLE advert with ANCS solicitation (AD type 0x15)");
    try
    {
        advertiser = new AncsAdvertiser(log.Trace);
        advertiser.Start();
        await Task.Delay(2000); // status flaps Waiting/Started while Windows arbitrates the advertising slot
        log.Info($"Publisher status: {advertiser.Status}");
        if (Args.Has(args, "--peripheral"))
            log.Info($"Connectable advert: {(await advertiser.StartConnectableAsync() ? "started" : "failed")}");
        log.Result("Solicitation advert", advertiser.Status.ToString());
    }
    catch (Exception e)
    {
        log.Fail($"{e.GetType().Name}: {e.Message}");
        log.Result("Solicitation advert", $"FAIL {e.GetType().Name}");
    }
}

log.Step("Paired devices");
var paired = await WindowsBluetooth.GetPairedDevicesAsync();
foreach (var d in paired) log.Info($"{(d.IsLowEnergy ? "LE     " : "Classic")} {d.AddressText} \"{d.Name}\" connected={d.IsConnected}");
string? filter = Args.Value(args, "--device");
bool Match(PairedDevice d) => filter is null ? d.LooksLikeIphone : d.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
var leEntry = paired.FirstOrDefault(d => d.IsLowEnergy && Match(d));
var classicEntry = paired.FirstOrDefault(d => !d.IsLowEnergy && Match(d));

BluetoothLEDevice? le = null;
if (leEntry is not null)
{
    le = await BluetoothLEDevice.FromIdAsync(leEntry.Id);
    log.Ok($"LE-paired entry found: \"{leEntry.Name}\" {leEntry.AddressText}");
    log.Result("iPhone LE bond", "OK LE entry exists");
}
else if (classicEntry is not null)
{
    log.Warn($"Only a Classic pairing for \"{classicEntry.Name}\". Trying LE on the same identity address {classicEntry.AddressText}...");
    le = await BluetoothLEDevice.FromBluetoothAddressAsync(classicEntry.Address);
    log.Result("iPhone LE bond", le is null ? "FAIL no LE entry, LE address unreachable" : "WARN no LE entry, LE device object obtained");
}
else
{
    log.Fail("No paired device named \"iPhone\".");
    log.Hint("Appairer l'iPhone : Paramètres Windows > Bluetooth et appareils > Ajouter un appareil > Bluetooth.");
    log.Result("iPhone LE bond", "FAIL not paired");
}

if (le is null)
{
    log.Hint("Voir docs/ancs-notes.md, plan B (Phone Link pour les notifs / clé USB / Raccourcis).");
    advertiser?.Dispose();
    log.PrintSummary();
    return 3;
}

using (le)
{
    log.Step("LE device state");
    var pairing = le.DeviceInformation.Pairing;
    log.Info($"Name \"{le.Name}\", address {PairedDevice.FormatAddress(le.BluetoothAddress)} ({le.BluetoothAddressType}), connection={le.ConnectionStatus}");
    log.Info($"Paired={pairing.IsPaired}, protection={pairing.ProtectionLevel}, canPair={pairing.CanPair}");

    if (!pairing.IsPaired && Args.Has(args, "--pair"))
    {
        log.Step("LE pairing from Windows");
        var custom = pairing.Custom;
        custom.PairingRequested += (_, e) =>
        {
            log.Info($"Pairing kind {e.PairingKind}, PIN {e.Pin}");
            if (e.PairingKind is DevicePairingKinds.ConfirmPinMatch or DevicePairingKinds.DisplayPin)
                log.Hint("Vérifier que le même code s'affiche sur l'iPhone et accepter sur l'iPhone.");
            e.Accept();
        };
        var r = await custom.PairAsync(DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch | DevicePairingKinds.DisplayPin,
            DevicePairingProtectionLevel.Encryption);
        log.Info($"PairAsync -> {r.Status}, protection {r.ProtectionLevelUsed}");
        log.Result("LE pairing", r.Status.ToString());
    }

    log.Step($"Looking for ANCS (up to {duration}s, retry every 5s)");
    log.Hint("Garder l'iPhone déverrouillé, Réglages > Bluetooth ouvert, près du PC.");
    var deadline = DateTime.UtcNow.AddSeconds(duration);
    string lastServices = "";
    bool found = false;
    while (DateTime.UtcNow < deadline && !found)
    {
        try
        {
            var r = await le.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            var names = string.Join(", ", r.Services.Select(s => KnownService(s.Uuid)));
            if (names != lastServices)
            {
                log.Info($"GATT status {r.Status}: {(r.Services.Count == 0 ? "(no service)" : names)}");
                lastServices = names;
            }
            found = r.Services.Any(s => s.Uuid == BluetoothIds.AncsService);
            foreach (var s in r.Services) s.Dispose();
        }
        catch (Exception e) { log.Warn($"GATT discovery: {e.GetType().Name}: {e.Message}"); }
        if (!found) await Task.Delay(5000);
    }
    log.Result("ANCS service visible", found ? "OK" : "FAIL not seen");

    if (!found)
    {
        log.Hint("ANCS absent : l'iPhone ne publie ANCS qu'aux appareils liés en LE. Essayer --pair, puis le plan B de docs/ancs-notes.md.");
    }
    else
    {
        log.Step("ANCS subscribe (Data Source, then Notification Source)");
        try
        {
            var transport = await GattAncsTransport.CreateAsync(le, log.Trace);
            await using var session = new AncsSession(transport, log.Trace);
            int total = 0, preexisting = 0;
            var perCategory = new Dictionary<AncsCategory, int>();
            session.NotificationReceived += (_, n) =>
            {
                Interlocked.Increment(ref total);
                if (n.IsPreExisting) Interlocked.Increment(ref preexisting);
                lock (perCategory) perCategory[n.Category] = perCategory.GetValueOrDefault(n.Category) + 1;
                log.Ok($"#{n.Uid} {n.Category} {n.AppIdentifier} ({n.AppDisplayName ?? "?"}) flags={n.Flags} date={n.Date:HH:mm:ss} " +
                       $"title {log.Content(n.Title)} subtitle {log.Content(n.Subtitle)} message {log.Content(n.Message)}");
            };
            session.IncomingCall += (_, n) => log.Ok($"INCOMING CALL #{n.Uid} actions: +\"{n.PositiveActionLabel}\" -\"{n.NegativeActionLabel}\"");
            session.NotificationRemoved += (_, uid) => log.Info($"removed #{uid}");
            session.ConnectionStateChanged += (_, s) => log.Info($"connection -> {s}");
            await session.StartAsync();
            log.Ok("Subscribed");
            log.Result("ANCS subscribe", "OK");
            log.Hint($"Envoie-toi un WhatsApp / Discord / SMS maintenant ; écoute pendant {duration} s (Ctrl+C pour arrêter).");
            await Task.Delay(TimeSpan.FromSeconds(duration));
            log.Result("ANCS notifications", total > 0
                ? $"OK {total} ({preexisting} pre-existing) " + string.Join(" ", perCategory.Select(kv => $"{kv.Key}={kv.Value}"))
                : "FAIL none received");
        }
        catch (AncsAccessDeniedException e)
        {
            log.Fail($"Access denied: {e.Message}");
            log.Hint("Lien LE non chiffré/non lié : relancer avec --pair, ou oublier le PC sur l'iPhone et réappairer.");
            log.Hint("Si l'iPhone le propose : accepter « Autoriser les notifications système ».");
            log.Result("ANCS subscribe", "FAIL access denied (not bonded)");
        }
        catch (Exception e)
        {
            log.Fail($"{e.GetType().Name}: {e.Message}");
            log.Result("ANCS subscribe", $"FAIL {e.GetType().Name}");
        }
    }
}

advertiser?.Dispose();
log.PrintSummary();
return 0;

static string KnownService(Guid uuid)
{
    if (uuid == BluetoothIds.AncsService) return "ANCS";
    if (uuid == new Guid("89D3502B-0F36-433A-8EF4-C502AD55F8DC")) return "AMS";
    var s = uuid.ToString();
    if (s.EndsWith("-0000-1000-8000-00805f9b34fb"))
    {
        var shortId = s[4..8].ToUpperInvariant();
        return shortId switch
        {
            "1800" => "GAP",
            "1801" => "GATT",
            "1805" => "CurrentTime",
            "180A" => "DeviceInfo",
            "180F" => "Battery",
            _ => "0x" + shortId,
        };
    }
    return s;
}
