using PhoneBridge.Bluetooth;
using PhoneBridge.Bluetooth.Map;
using PhoneBridge.Bluetooth.Obex;
using PhoneBridge.Bluetooth.Pbap;
using PhoneBridge.Bluetooth.Platform;
using PhoneBridge.Spikes;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;

// SPIKE MAP/PBAP (SPEC §6 piste 1). Step by step, every step logged; content masked unless --verbose.
if (Args.Has(args, "--help") || Args.Has(args, "-h"))
{
    Console.WriteLine("""
        spike-map [--device <name part>] [--count 10] [--send <number|email> [--text "..."] [--yes]]
                  [--mns-wait 60] [--no-mns] [--no-pbap] [--verbose]
          --device    pick the paired device whose name contains this (default: first "iPhone")
          --send      send one test message with PushMessage (asks for confirmation unless --yes)
          --mns-wait  seconds to wait for real-time "new message" events (send yourself an SMS meanwhile)
          --verbose   show message text, names and numbers (the log file then contains private data)
        """);
    return 0;
}

using var log = new SpikeLog("spike-map", Args.Has(args, "--verbose"));
int count = Args.Int(args, "--count", 10);

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
log.Info($"Address {adapter.Address}, radio {adapter.RadioState}, Classic={adapter.ClassicSupported}, LE={adapter.LowEnergySupported}");
log.Result("Adapter", adapter.ClassicSupported ? $"OK {adapter.Address} radio={adapter.RadioState}" : "FAIL no Classic (MAP needs BR/EDR)");
if (!adapter.ClassicSupported) { log.PrintSummary(); return 2; }

log.Step("Paired devices");
var paired = await WindowsBluetooth.GetPairedDevicesAsync();
foreach (var d in paired) log.Info($"{(d.IsLowEnergy ? "LE     " : "Classic")} {d.AddressText} \"{d.Name}\" connected={d.IsConnected}");
if (paired.Count == 0) log.Warn("No paired device at all.");
string? filter = Args.Value(args, "--device");
var target = paired.FirstOrDefault(d => !d.IsLowEnergy && (filter is null ? d.LooksLikeIphone : d.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)));
if (target is null)
{
    log.Fail(filter is null ? "No paired Classic device named \"iPhone\"." : $"No paired Classic device matching \"{filter}\".");
    log.Hint("Appairer l'iPhone : Paramètres Windows > Bluetooth et appareils > Ajouter un appareil > Bluetooth, puis relancer.");
    log.Hint("Si l'iPhone a un autre nom : spike-map --device \"<partie du nom>\"");
    log.Result("iPhone paired (Classic)", "FAIL not found");
    log.PrintSummary();
    return 3;
}
log.Ok($"Using \"{target.Name}\" {target.AddressText}");
log.Result("iPhone paired (Classic)", $"OK {target.AddressText}");

using var device = await BluetoothDevice.FromIdAsync(target.Id);
if (device is null)
{
    log.Fail("BluetoothDevice.FromIdAsync returned null (access denied by Windows privacy settings?)");
    log.Hint("Paramètres Windows > Confidentialité et sécurité > Autres appareils : autoriser les applications de bureau.");
    log.Result("Open device", "FAIL null");
    log.PrintSummary();
    return 4;
}
log.Info($"ClassOfDevice major={device.ClassOfDevice.MajorClass} minor={device.ClassOfDevice.MinorClass} raw=0x{device.ClassOfDevice.RawValue:X6}, connection={device.ConnectionStatus}");

log.Step("RFCOMM services on the iPhone (fresh SDP query)");
IReadOnlyList<RfcommDeviceService> services = [];
try
{
    services = await WindowsBluetooth.GetRfcommServicesAsync(device);
    foreach (var s in services)
    {
        var shortId = s.ServiceId.AsShortId();
        log.Info($"0x{shortId:X4} {ServiceName(shortId)}  ({s.ConnectionServiceName})");
    }
    if (services.Count == 0) log.Warn("No RFCOMM service. Is the iPhone unlocked and near the PC? Bluetooth on?");
}
catch (Exception e)
{
    log.Fail($"SDP query failed: {e.Message}");
    log.Hint("Déverrouiller l'iPhone, Réglages > Bluetooth ouvert, puis relancer.");
}
var mas = services.FirstOrDefault(s => s.ServiceId.AsShortId() == BluetoothIds.MapMasShortId);
var pse = services.FirstOrDefault(s => s.ServiceId.AsShortId() == BluetoothIds.PbapPseShortId);
log.Result("SDP: MAS (0x1132)", mas is null ? "FAIL absent" : "OK found");
log.Result("SDP: PBAP PSE (0x112F)", pse is null ? "FAIL absent" : "OK found");
foreach (var (name, svc) in new[] { ("MAS", mas), ("PBAP", pse) })
{
    if (svc is null) continue;
    try
    {
        foreach (var (id, value) in await WindowsBluetooth.ReadSdpAsync(svc))
            log.Info($"{name} SDP {Sdp.AttributeName(id)} = {value}");
    }
    catch (Exception e) { log.Warn($"{name} SDP attributes unreadable: {e.Message}"); }
}

MnsServer? mns = null;
MnsServiceHost? mnsHost = null;
if (!Args.Has(args, "--no-mns"))
{
    log.Step("Publish MNS (0x1133) on this PC, for real-time events");
    try
    {
        mns = new MnsServer(log.Trace);
        mnsHost = await MnsServiceHost.StartAsync(mns, log.Trace);
        log.Ok("MNS SDP record published");
        log.Result("MNS published", "OK");
    }
    catch (Exception e)
    {
        log.Fail($"{e.GetType().Name}: {e.Message}");
        log.Hint("Phone Link (Lien avec Windows) publie peut-être déjà un MNS : le délier/fermer et relancer.");
        log.Result("MNS published", $"FAIL {e.GetType().Name}");
        mns = null;
    }
}

if (mas is not null)
{
    log.Step("MAP: OBEX Connect to MAS");
    var map = new MapClient(ct => WindowsBluetooth.ConnectAsync(mas, ct), log.Trace);
    bool connected = false;
    try
    {
        await map.ConnectAsync();
        connected = true;
        log.Ok("Connected");
        log.Result("MAP OBEX connect", "OK");
    }
    catch (ObexException e) when (e.Code == ObexResponseCode.Forbidden)
    {
        log.Fail("Forbidden (0x43): the iPhone refused.");
        log.Hint("iPhone : Réglages > Bluetooth > (i) à côté du PC > activer « Afficher les notifications » / « Show Message Notifications ».");
        log.Hint("Si l'option n'apparaît pas : délier Phone Link, oublier le PC sur l'iPhone, réappairer, rouvrir (i) plusieurs fois.");
        log.Result("MAP OBEX connect", "FAIL Forbidden 0x43 (toggle off)");
    }
    catch (Exception e)
    {
        log.Fail($"{e.GetType().Name}: {e.Message}");
        log.Hint("« Connection refused » = un autre client MAP (Phone Link) tient la session : le délier puis relancer.");
        log.Result("MAP OBEX connect", $"FAIL {e.GetType().Name}");
    }

    if (connected)
    {
        string inbox = "inbox";
        await Try("MAP folder listing", async () =>
        {
            foreach (var path in new[] { "", "telecom", "telecom/msg" })
            {
                var folders = await map.GetFolderListingAsync(path);
                log.Info($"/{path}: {string.Join(", ", folders.Select(f => f.Name))}");
                if (path == "telecom/msg")
                    inbox = folders.FirstOrDefault(f => f.Name.Equals("inbox", StringComparison.OrdinalIgnoreCase))?.Name ?? inbox;
            }
            return $"OK inbox folder = \"{inbox}\"";
        });

        await Try($"MAP list {count} messages", async () =>
        {
            var list = await map.GetMessagesListingAsync(inbox, count);
            var p = map.LastListingParameters;
            log.Info($"ListingSize={p?.GetNumber(MapTag.ListingSize)?.ToString() ?? "?"} NewMessage={p?.GetNumber(MapTag.NewMessage)?.ToString() ?? "?"}");
            foreach (var m in list)
                log.Info($"{m.Handle} {m.DateTime:yyyy-MM-dd HH:mm} type={m.Type} read={m.Read} from {log.Content(m.SenderName)} {log.Address(m.SenderAddressing)} subject {log.Content(m.Subject)} size={m.Size}");
            return list.Count > 0 ? $"OK {list.Count} message(s)" : "WARN 0 message (inbox empty over MAP?)";
        });

        var sendTo = Args.Value(args, "--send");
        if (sendTo is not null)
        {
            var text = Args.Value(args, "--text") ?? $"Test PhoneBridge {DateTime.Now:HH:mm}";
            log.Step("MAP PushMessage (send)");
            bool go = Args.Has(args, "--yes");
            if (!go)
            {
                Console.Write($"   Envoyer un message de {text.Length} caractères à {log.Address(sendTo)} ? (o/N) ");
                go = (Console.ReadLine() ?? "").Trim().StartsWith("o", StringComparison.OrdinalIgnoreCase);
            }
            if (!go) log.Result("MAP send", "SKIPPED by user");
            else await Try("MAP send", async () =>
            {
                var handle = await map.PushMessageAsync(sendTo, text);
                log.Hint("Vérifier sur l'iPhone que le message est parti (bulle bleue = iMessage, verte = SMS).");
                return $"OK handle={handle ?? "(none)"}";
            });
        }

        if (mns is not null)
        {
            int wait = Args.Int(args, "--mns-wait", 60);
            log.Step($"MAP real-time events: register, wait {wait}s");
            int events = 0, messages = 0;
            mns.EventReceived += (_, e) => Interlocked.Increment(ref events);
            map.AttachMns(mns);
            map.MessageReceived += (_, m) =>
            {
                Interlocked.Increment(ref messages);
                log.Ok($"New message {m.Handle} from {log.Content(m.SenderName)} {log.Address(m.SenderAddress)}: {log.Content(m.Body)}");
            };
            bool registered = await Try("MAP notification registration", async () =>
            {
                await map.SetNotificationRegistrationAsync(true);
                return "OK";
            });
            if (registered)
            {
                log.Hint($"Envoie-toi un SMS ou iMessage depuis un autre téléphone maintenant ({wait} s)...");
                await Task.Delay(TimeSpan.FromSeconds(wait));
                log.Result("MNS events received", events > 0 ? $"OK {events} event(s), {messages} message(s)" : "FAIL none (did the iPhone connect to MNS?)");
                try { await map.SetNotificationRegistrationAsync(false); } catch (Exception) { }
            }
        }
        await map.DisconnectAsync();
    }
}

if (pse is not null && !Args.Has(args, "--no-pbap"))
{
    log.Step("PBAP: contacts and call history");
    var pbap = new PbapClient(ct => WindowsBluetooth.ConnectAsync(pse, ct), log.Trace);
    bool ok = await Try("PBAP OBEX connect", async () => { await pbap.ConnectAsync(); return "OK"; });
    if (ok)
    {
        await Try("PBAP phonebook size", async () => $"OK {(await pbap.GetPhonebookSizeAsync())?.ToString() ?? "?"}");
        await Try("PBAP pull contacts", async () =>
        {
            var cards = await pbap.PullPhonebookAsync(offset: 1);
            foreach (var c in cards.Take(5)) log.Info($"{log.Content(c.DisplayName)} phones={c.Phones.Count} emails={c.Emails.Count} photo={(c.Photo is null ? "no" : c.Photo.Length + "B")}");
            return $"OK {cards.Count} contact(s), {cards.Count(c => c.Photo is not null)} with photo";
        });
        await Try("PBAP missed calls (mch)", async () =>
        {
            var calls = await pbap.PullPhonebookAsync("telecom/mch.vcf");
            return $"OK {calls.Count} entr(y/ies), first type={calls.FirstOrDefault()?.CallType ?? "-"}";
        });
        await pbap.DisconnectAsync();
    }
    else log.Hint("Forbidden = « Synchroniser les contacts » désactivé dans Réglages > Bluetooth > (i).");
}

if (mnsHost is not null) await mnsHost.DisposeAsync();
log.PrintSummary();
return 0;

async Task<bool> Try(string step, Func<Task<string>> action)
{
    try
    {
        var result = await action();
        (result.StartsWith("OK") ? (Action<string>)log.Ok : log.Warn)($"{step}: {result}");
        log.Result(step, result);
        return true;
    }
    catch (ObexException e)
    {
        log.Fail($"{step}: {e.Message}");
        log.Result(step, $"FAIL {e.Code}");
    }
    catch (Exception e)
    {
        log.Fail($"{step}: {e.GetType().Name}: {e.Message}");
        log.Result(step, $"FAIL {e.GetType().Name}");
    }
    return false;
}

static string ServiceName(uint id) => id switch
{
    0x1101 => "Serial Port",
    0x1105 => "OBEX Object Push",
    0x1106 => "OBEX File Transfer",
    0x110A => "A2DP Source",
    0x110C => "AVRCP Target",
    0x110E => "AVRCP",
    0x1112 => "Headset AG",
    0x111F => "Hands-Free Audio Gateway",
    0x112F => "PBAP PSE  <- contacts",
    0x1132 => "MAP MAS   <- messages",
    0x1133 => "MAP MNS",
    0x1200 => "PnP Information",
    _ => "",
};
