// Fake dataset for the browser preview (`npm run dev`). Never used inside the WebView2 host.
import type { Call, ClipboardItem, Conversation, Message, PhoneNotification, Snapshot, Transfer } from './types';

export type Scenario = 'populated' | 'empty' | 'disconnected' | 'unpaired' | 'error' | 'call';

export function parseScenario(value: string | null): Scenario {
  const all: Scenario[] = ['populated', 'empty', 'disconnected', 'unpaired', 'error', 'call'];
  return all.includes(value as Scenario) ? (value as Scenario) : 'populated';
}

const MB = 1024 * 1024;

export function createDataset(scenario: Scenario, now = Date.now()) {
  const ago = (min: number) => new Date(now - min * 60_000).toISOString();

  const conversations: Conversation[] = [
    { id: 'c-maman', name: 'Maman', handle: '+33 6 11 22 33 44', service: 'imessage', lastMessage: 'Tu rentres à quelle heure ce soir ?', lastAt: ago(3), unread: 2 },
    { id: 'c-lea', name: 'Léa Martin', handle: 'lea.martin@icloud.com', service: 'imessage', lastMessage: 'Trop bien, on se voit demain au CDI 📚', lastAt: ago(26), unread: 0 },
    { id: 'c-otp', name: '38200', handle: '38200', service: 'sms', lastMessage: 'Votre code de vérification est 482193. Il expire dans 10 minutes.', lastAt: ago(41), unread: 1 },
    { id: 'c-hugo', name: 'Hugo', handle: '+33 7 81 45 90 12', service: 'sms', lastMessage: 'Tu as fini l’exo 3 de maths ?', lastAt: ago(95), unread: 0 },
    { id: 'c-papa', name: 'Papa', handle: '+33 6 55 66 77 88', service: 'imessage', lastMessage: 'Je passe te chercher à 18 h devant le lycée.', lastAt: ago(60 * 5), unread: 0 },
    { id: 'c-ines', name: 'Inès Leroy', handle: '+33 6 02 03 04 05', service: 'imessage', lastMessage: 'Merci pour le partage du cours !', lastAt: ago(60 * 24 + 30), unread: 0 },
    { id: 'c-coach', name: 'Coach basket', handle: '+33 6 98 76 54 32', service: 'sms', lastMessage: 'Entraînement décalé à 19 h samedi.', lastAt: ago(60 * 24 * 2), unread: 0 },
    { id: 'c-mamie', name: 'Mamie', handle: '+33 2 99 12 34 56', service: 'sms', lastMessage: 'Bisous mon grand, bon courage pour le bac blanc', lastAt: ago(60 * 24 * 4), unread: 0 },
    { id: 'c-nathan', name: 'Nathan', handle: '+33 7 66 55 44 33', service: 'imessage', lastMessage: 'Ok ça marche 👍', lastAt: ago(60 * 24 * 9), unread: 0 },
  ];

  const threads: Record<string, Message[]> = {};
  const add = (conversationId: string, fromMe: boolean, body: string, minAgo: number, extra: Partial<Message> = {}) => {
    const conv = conversations.find((c) => c.id === conversationId)!;
    (threads[conversationId] ??= []).push({
      id: `${conversationId}-${(threads[conversationId]?.length ?? 0) + 1}`,
      conversationId, fromMe, body, at: ago(minAgo), service: conv.service, ...extra,
    });
  };
  add('c-maman', false, 'Coucou, tu as pensé à prendre ta carte de bus ?', 60 * 3);
  add('c-maman', true, 'Oui c’est bon, elle est dans mon sac', 60 * 3 - 2, { status: 'sent', via: 'bluetooth' });
  add('c-maman', false, 'Super. Mamie vient dîner dimanche', 60 * 3 - 5);
  add('c-maman', true, 'Cool ! Je lui montrerai mon projet NSI', 60 * 3 - 6, { status: 'sent', via: 'bluetooth' });
  add('c-maman', false, 'Elle va adorer 😊', 8);
  add('c-maman', false, 'Tu rentres à quelle heure ce soir ?', 3);
  add('c-lea', false, 'Tu as compris le chapitre sur les graphes ?', 60 * 2);
  add('c-lea', true, 'Oui, Dijkstra c’est plus simple que ça en a l’air', 60 * 2 - 3, { status: 'sent', via: 'bluetooth' });
  add('c-lea', true, 'Je te fais une fiche si tu veux', 60 * 2 - 3, { status: 'sent', via: 'bluetooth' });
  add('c-lea', false, 'Oh oui stp !! Tu es le meilleur', 45);
  add('c-lea', true, 'Je te l’envoie ce soir', 30, { status: 'sent', via: 'bluetooth' });
  add('c-lea', false, 'Trop bien, on se voit demain au CDI 📚', 26);
  add('c-otp', false, 'Votre code de vérification est 482193. Il expire dans 10 minutes.', 41);
  add('c-hugo', false, 'Salut, tu as fini l’exo 3 de maths ?', 100);
  add('c-hugo', true, 'Presque, il me reste la dernière question', 98, { status: 'failed', error: 'iPhone hors de portée' });
  add('c-hugo', false, 'Tu as fini l’exo 3 de maths ?', 95);
  for (const c of conversations) if (!threads[c.id]) add(c.id, false, c.lastMessage, (now - Date.parse(c.lastAt)) / 60_000);

  const notifications: PhoneNotification[] = [
    { id: 'n1', appId: 'net.whatsapp.WhatsApp', appName: 'WhatsApp', category: 'social', title: 'Terminale NSI', subtitle: 'Yanis', body: 'Quelqu’un a le sujet du projet pour lundi ?', at: ago(2), positiveAction: 'Répondre', negativeAction: 'Effacer' },
    { id: 'n2', appId: 'net.whatsapp.WhatsApp', appName: 'WhatsApp', category: 'social', title: 'Léa Martin', body: '🎤 Message vocal (0:12)', at: ago(18), negativeAction: 'Effacer' },
    { id: 'n3', appId: 'com.hammerandchisel.discord', appName: 'Discord', category: 'social', title: '#général — Club info', body: 'Thomas : la réunion est déplacée à jeudi 13 h', at: ago(12), negativeAction: 'Effacer' },
    { id: 'n4', appId: 'com.hammerandchisel.discord', appName: 'Discord', category: 'social', title: 'Hugo', body: 'tu viens en vocal ?', at: ago(50), negativeAction: 'Effacer' },
    { id: 'n5', appId: 'com.index-education.pronote', appName: 'Pronote', category: 'news', title: 'Nouvelle note', body: 'Mathématiques : 17,5/20 — Devoir surveillé n°4', at: ago(35), negativeAction: 'Effacer' },
    { id: 'n6', appId: 'com.google.Gmail', appName: 'Gmail', category: 'email', title: 'Parcoursup', body: 'Rappel : confirmez vos vœux avant le 12 mars', at: ago(60 * 3), negativeAction: 'Effacer' },
    { id: 'n7', appId: 'com.instagram', appName: 'Instagram', category: 'social', title: 'Instagram', body: 'nathan.dlc a aimé votre publication', at: ago(60 * 4), negativeAction: 'Effacer' },
    { id: 'n8', appId: 'com.vsct.sncfconnect', appName: 'SNCF Connect', category: 'other', title: 'Votre train', body: 'TER Rennes → Saint-Malo : voie 3, départ 17:42', at: ago(60 * 6), negativeAction: 'Effacer' },
  ];

  const transfers: Transfer[] = [
    { id: 't1', name: 'Vidéo vacances.mov', size: 512 * MB, transferred: 214 * MB, direction: 'toPc', status: 'active', at: ago(1) },
    { id: 't2', name: 'IMG_2041.HEIC', size: 3.2 * MB, transferred: 3.2 * MB, direction: 'toPc', status: 'done', at: ago(14) },
    { id: 't3', name: 'Rapport projet NSI.pdf', size: 1.8 * MB, transferred: 1.8 * MB, direction: 'toPhone', status: 'done', at: ago(60 * 2) },
    { id: 't4', name: 'Playlist révisions.zip', size: 88 * MB, transferred: 31 * MB, direction: 'toPhone', status: 'failed', at: ago(60 * 5), error: 'iPhone hors de portée' },
    { id: 't5', name: 'Affiche gala.jpg', size: 0.9 * MB, transferred: 0.9 * MB, direction: 'toPc', status: 'done', at: ago(60 * 26) },
  ];

  const thumb = 'data:image/svg+xml,' + encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" width="96" height="64"><defs><linearGradient id="g" x2="1" y2="1"><stop offset="0" stop-color="#ffb36b"/><stop offset="1" stop-color="#ff5e7e"/></linearGradient></defs><rect width="96" height="64" fill="url(#g)"/><circle cx="70" cy="20" r="9" fill="#fff6"/><path d="M0 64 30 34l18 16 14-10 34 24z" fill="#0004"/></svg>');
  const clipboard: ClipboardItem[] = [
    { id: 'k1', kind: 'url', text: 'https://www.parcoursup.gouv.fr/', origin: 'phone', at: ago(4) },
    { id: 'k2', kind: 'text', text: 'def dijkstra(graphe, depart):\n    distances = {s: float("inf") for s in graphe}', origin: 'pc', at: ago(22) },
    { id: 'k3', kind: 'image', imageDataUrl: thumb, origin: 'phone', at: ago(60) },
    { id: 'k4', kind: 'text', text: '12 rue de la Monnaie, 35000 Rennes', origin: 'phone', at: ago(60 * 3) },
  ];

  const calls: Call[] = [
    { id: 'p1', name: 'Maman', handle: '+33 6 11 22 33 44', kind: 'incoming', at: ago(40), durationSec: 252 },
    { id: 'p2', name: 'Léa Martin', handle: '+33 6 45 45 45 45', kind: 'missed', at: ago(60 * 2) },
    { id: 'p3', name: 'Papa', handle: '+33 6 55 66 77 88', kind: 'outgoing', at: ago(60 * 5), durationSec: 61 },
    { id: 'p4', name: '+33 9 70 80 90 10', handle: '+33 9 70 80 90 10', kind: 'missed', at: ago(60 * 26) },
    { id: 'p5', name: 'Hugo', handle: '+33 7 81 45 90 12', kind: 'incoming', at: ago(60 * 50), durationSec: 1240 },
  ];

  const snapshot: Snapshot = {
    host: { version: '0.1.0-preview', mica: false, maximized: false },
    connection: { paired: true, phoneName: 'iPhone d’Arthur', pcName: 'PC d’Arthur', wifi: 'connected', bluetooth: 'connected', lastSeenAt: ago(0) },
    battery: { level: 76, charging: true, updatedAt: ago(5), source: 'shortcut' },
    conversations, notifications, transfers, clipboard, calls,
    devices: [{ id: 'd1', name: 'iPhone d’Arthur', pairedAt: ago(60 * 24 * 12), lastSeenAt: ago(0), current: true }],
    settings: {
      windowStyle: 'mac', theme: 'system', toasts: true, clipboardAuto: false, otpAutoCopy: true,
      keepHistory: true, helloLock: false, shortcutsHttp: true, bluetooth: true, downloadDir: 'C:\\Users\\arthu\\Downloads\\PhoneBridge',
    },
  };

  if (scenario === 'empty' || scenario === 'unpaired') {
    Object.assign(snapshot, { conversations: [], notifications: [], transfers: [], clipboard: [], calls: [] });
    for (const k of Object.keys(threads)) delete threads[k];
    snapshot.battery = { level: null, charging: false, updatedAt: null };
  }
  if (scenario === 'unpaired') {
    snapshot.connection = { ...snapshot.connection, paired: false, wifi: 'disconnected', bluetooth: 'disconnected', lastSeenAt: null };
    snapshot.devices = [];
  }
  if (scenario === 'disconnected') {
    snapshot.connection = { ...snapshot.connection, wifi: 'disconnected', bluetooth: 'disconnected', lastSeenAt: ago(47) };
    snapshot.battery = { level: 18, charging: false, updatedAt: ago(52), source: 'shortcut' };
    snapshot.transfers = transfers.map((t) => (t.status === 'active' ? { ...t, status: 'failed', error: 'iPhone hors de portée' } : t));
  }
  if (scenario === 'call') {
    snapshot.calls = [{ id: 'p0', name: 'Léa Martin', handle: '+33 6 45 45 45 45', kind: 'incoming', at: ago(0), ringing: true }, ...calls];
  }

  return { snapshot, threads };
}
