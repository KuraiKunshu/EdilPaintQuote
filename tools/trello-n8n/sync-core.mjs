// Shared pure functions embedded in the n8n Code nodes by build-workflow.mjs.
// No network access, credentials, customer matching, or quote writes belong here.
export const PAGE_SIZE = 1000;
export const MAX_PAGES = 200;
export const COMMENTS_REFRESH_MS = 24 * 60 * 60 * 1000;

export function canonical(value) {
  if (Array.isArray(value)) return '[' + value.map(canonical).join(',') + ']';
  if (value !== null && typeof value === 'object') {
    return '{' + Object.keys(value).sort().map(key => JSON.stringify(key) + ':' + canonical(value[key])).join(',') + '}';
  }
  return JSON.stringify(value);
}

export function asArray(value, description) {
  if (!Array.isArray(value)) throw new Error(description + ': risposta non valida; sincronizzazione interrotta senza cancellazioni.');
  return value;
}

export function iso(value) {
  if (value === null || value === undefined || value === '') return null;
  const parsed = new Date(value);
  if (Number.isNaN(parsed.valueOf())) throw new Error('Data Trello non valida: ' + value);
  return parsed.toISOString();
}

export function withoutSync(payload) {
  const source = { ...payload };
  delete source.actions;
  delete source._sync;
  return source;
}

export function validatePages(pages, description) {
  const seen = new Set();
  const output = [];
  for (const page of pages) {
    asArray(page, description);
    for (const item of page) {
      if (!item || typeof item.id !== 'string' || !item.id) throw new Error(description + ': ID mancante.');
      if (seen.has(item.id)) throw new Error(description + ': pagina ripetuta o dati cambiati durante la lettura. Ripetere la sincronizzazione.');
      seen.add(item.id);
      output.push(item);
    }
  }
  if (pages.length >= MAX_PAGES && pages.at(-1)?.length === PAGE_SIZE) {
    throw new Error(description + ': raggiunto limite di paginazione. Aumentare MAX_PAGES prima di proseguire.');
  }
  return output;
}

export function prepareCard(card, board, existing, config) {
  if (!card.id || card.idBoard !== board.id) throw new Error('Scheda senza ID o appartenente a una bacheca differente.');
  if (typeof card.name !== 'string' || typeof card.closed !== 'boolean') throw new Error('Titolo o stato della scheda mancanti.');
  const list = asArray(board.lists, 'Liste bacheca').find(item => item.id === card.idList);
  if (!list) throw new Error('Lista della scheda non presente nei metadati; ripetere la sincronizzazione.');
  for (const checklist of asArray(card.checklists, 'Checklist scheda')) asArray(checklist.checkItems, 'Voci checklist');
  asArray(card.customFieldItems, 'Campi personalizzati scheda');
  asArray(card.attachments, 'Allegati scheda');
  const definitions = asArray(board.customFields, 'Definizioni campi personalizzati');
  const boardMembers = asArray(board.members, 'Membri bacheca');
  const payload = {
    ...card,
    board: { id: board.id, name: board.name, url: board.url, closed: board.closed },
    list: { id: list.id, name: list.name, closed: list.closed },
    customFields: definitions,
    members: Array.isArray(card.members) ? card.members : boardMembers.filter(member => (card.idMembers || []).includes(member.id)),
  };
  let previous = null;
  try { previous = existing ? JSON.parse(existing.PayloadJson) : null; } catch { /* Repair malformed mirror with a complete read. */ }
  const unchanged = previous && canonical(withoutSync(previous)) === canonical(withoutSync(payload));
  const checkedAt = previous?._sync?.commentsFetchedAtUtc;
  const checkedTime = checkedAt ? new Date(checkedAt).valueOf() : NaN;
  const elapsed = new Date(config.syncStartedAtUtc).valueOf() - checkedTime;
  const zeroComments = card.badges && card.badges.comments === 0;
  const needsComments = !zeroComments && (config.forceCommentsRefresh || !unchanged || !Array.isArray(previous?.actions) || !Number.isFinite(checkedTime) || elapsed >= COMMENTS_REFRESH_MS);
  return {
    sourcePayload: withoutSync(payload),
    cachedActions: zeroComments ? [] : (Array.isArray(previous?.actions) ? previous.actions : []),
    commentsFetchedAtUtc: zeroComments ? config.syncStartedAtUtc : checkedAt,
    needsComments: Boolean(needsComments),
    syncStartedAtUtc: config.syncStartedAtUtc,
  };
}

export function finishCard(prepared, comments) {
  const source = prepared.sourcePayload;
  const actions = comments === undefined ? prepared.cachedActions : comments;
  asArray(actions, 'Commenti scheda');
  const payload = {
    ...source,
    actions,
    _sync: {
      version: 1,
      commentsFetchedAtUtc: comments === undefined ? prepared.commentsFetchedAtUtc : prepared.syncStartedAtUtc,
      binariesDownloaded: false,
    },
  };
  const url = source.url || source.shortUrl || '';
  if (!/^https:\/\/trello\.com\/c\/[A-Za-z0-9]+(?:\/|$)/.test(url)) throw new Error('URL scheda Trello non valido.');
  return {
    Id: source.id,
    BoardId: source.idBoard,
    BoardName: source.board.name || '',
    ListId: source.idList,
    ListName: source.list.name || '',
    Name: source.name || '',
    Description: source.desc || '',
    Url: url,
    IsArchived: Boolean(source.closed),
    IsDeleted: false,
    DueUtc: iso(source.due),
    LastActivityUtc: iso(source.dateLastActivity),
    SyncedAtUtc: iso(prepared.syncStartedAtUtc),
    PayloadJson: JSON.stringify(payload),
  };
}
