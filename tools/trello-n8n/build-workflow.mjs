import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as core from './sync-core.mjs';

const directory = path.dirname(fileURLToPath(import.meta.url));
const functions = ['canonical', 'asArray', 'iso', 'withoutSync', 'validatePages', 'prepareCard', 'finishCard'];
const shared = `const PAGE_SIZE = ${core.PAGE_SIZE};\nconst MAX_PAGES = ${core.MAX_PAGES};\nconst COMMENTS_REFRESH_MS = ${core.COMMENTS_REFRESH_MS};\n` + functions.map(name => core[name].toString()).join('\n');
const node = (name, type, typeVersion, position, parameters, extra = {}) => ({ id: name.toLowerCase().replace(/[^a-z0-9]+/g, '-'), name, type, typeVersion, position, parameters, ...extra });
const code = (name, position, body) => node(name, 'n8n-nodes-base.code', 2, position, { mode: 'runOnceForAllItems', jsCode: shared + '\n' + body });
const httpOptions = {
  timeout: 120000,
  batching: { batch: { batchSize: 1, batchInterval: 500 } },
  response: { response: { responseFormat: 'json', fullResponse: true, neverError: false } },
};
const paginated = {
  ...httpOptions,
  pagination: { pagination: {
    paginationMode: 'updateAParameterInEachRequest',
    parameters: { parameters: [{ type: 'qs', name: 'before', value: "={{ $pageCount === 0 ? $('Configurazione').first().json.syncStartedAtUtc : $response.body[$response.body.length - 1].id }}" }] },
    paginationCompleteWhen: 'other',
    completeExpression: `={{ Array.isArray($response.body) && $response.body.length < ${core.PAGE_SIZE} }}`,
    limitPagesFetched: true,
    maxRequests: core.MAX_PAGES,
    requestInterval: 500,
  } },
};
const http = (name, position, url, query, options) => node(name, 'n8n-nodes-base.httpRequest', 4.2, position, {
  method: 'GET', url, authentication: 'predefinedCredentialType', nodeCredentialType: 'trelloApi',
  sendQuery: true, queryParameters: { parameters: Object.entries(query).map(([name, value]) => ({ name, value })) },
  options,
}, { retryOnFail: true, maxTries: 5, waitBetweenTries: 5000 });

const nodes = [
  node('Avvio manuale', 'n8n-nodes-base.manualTrigger', 1, [0, 0], {}),
  node('Ogni 15 minuti', 'n8n-nodes-base.scheduleTrigger', 1.2, [0, 200], { rule: { interval: [{ field: 'minutes', minutesInterval: 15 }] } }),
  node('Configurazione', 'n8n-nodes-base.code', 2, [240, 100], {
    mode: 'runOnceForAllItems',
    jsCode: "// Aggiungere qui soltanto gli ID delle bacheche da leggere. Nessuna associazione.\nconst boardIds = ['68eb83dcac50df446b956713']; // Sopralluoghi\nconst forceCommentsRefresh = false; // true solo per una rilettura straordinaria\nif (!boardIds.length || boardIds.some(id => !/^[a-f0-9]{24}$/.test(id))) throw new Error('Configurare ID completi delle bacheche Trello.');\nreturn [{ json: { boardIds: [...new Set(boardIds)], syncStartedAtUtc: new Date().toISOString(), forceCommentsRefresh } }];",
  }),
  node('Leggi copia esistente', 'n8n-nodes-base.postgres', 2.5, [480, 100], {
    operation: 'executeQuery',
    query: 'SELECT COALESCE(json_agg(json_build_object(\'Id\', "Id", \'PayloadJson\', "PayloadJson")), \'[]\'::json) AS existing FROM public."TrelloCards" WHERE "BoardId" IN (SELECT jsonb_array_elements_text($1::jsonb));',
    options: { queryReplacement: '={{ [JSON.stringify($("Configurazione").first().json.boardIds)] }}' },
  }, { retryOnFail: true, maxTries: 3, waitBetweenTries: 5000 }),
  node('Bacheche da leggere', 'n8n-nodes-base.code', 2, [720, 100], {
    mode: 'runOnceForAllItems', jsCode: "return $('Configurazione').first().json.boardIds.map(boardId => ({ json: { boardId } }));",
  }),
  http('Leggi bacheche', [960, 100], "={{ 'https://api.trello.com/1/boards/' + $json.boardId }}", {
    fields: 'all', lists: 'all', list_fields: 'all', customFields: 'true', members: 'all', member_fields: 'all',
  }, { ...httpOptions, response: { response: { responseFormat: 'json', fullResponse: false, neverError: false } } }),
  http('Leggi tutte le schede', [1200, 100], "={{ 'https://api.trello.com/1/boards/' + $json.id + '/cards/all' }}", {
    fields: 'all', limit: String(core.PAGE_SIZE), sort: '-id', attachments: 'true', attachment_fields: 'all',
    customFieldItems: 'true', checklists: 'all', checklist_fields: 'all', checkItemStates: 'true', members: 'true', member_fields: 'all',
  }, paginated),
  code('Prepara schede', [1440, 100], `
const config = $('Configurazione').first().json;
const existingRows = asArray($('Leggi copia esistente').first().json.existing, 'Copia database');
const existing = new Map(existingRows.map(row => [row.Id, row]));
const groups = new Map();
for (const [index, item] of $input.all().entries()) {
  const board = $('Leggi bacheche').itemMatching(index).json;
  if (!groups.has(board.id)) groups.set(board.id, { board, pages: [] });
  groups.get(board.id).pages.push(asArray(item.json.body, 'Schede bacheca'));
}
const result = [];
for (const { board, pages } of groups.values()) {
  for (const card of validatePages(pages, 'Schede bacheca ' + board.id)) {
    result.push({ json: prepareCard(card, board, existing.get(card.id), config) });
  }
}
return result;`),
  node('Rileggere commenti', 'n8n-nodes-base.if', 2.2, [1680, 100], {
    conditions: { options: { caseSensitive: true, leftValue: '', typeValidation: 'strict', version: 2 },
      conditions: [{ id: 'read-comments', leftValue: '={{ $json.needsComments }}', rightValue: '', operator: { type: 'boolean', operation: 'true', singleValue: true } }], combinator: 'and' }, options: {},
  }),
  http('Leggi commenti completi', [1920, -40], "={{ 'https://api.trello.com/1/cards/' + $json.sourcePayload.id + '/actions' }}", {
    filter: 'commentCard,copyCommentCard', limit: String(core.PAGE_SIZE), fields: 'all', memberCreator: 'true', memberCreator_fields: 'all',
  }, paginated),
  code('Schede con commenti aggiornati', [2160, -40], `
const groups = new Map();
for (const [index, item] of $input.all().entries()) {
  const prepared = $('Rileggere commenti').itemMatching(index).json;
  const id = prepared.sourcePayload.id;
  if (!groups.has(id)) groups.set(id, { prepared, pages: [] });
  groups.get(id).pages.push(asArray(item.json.body, 'Commenti scheda'));
}
return [...groups.values()].map(({ prepared, pages }) => ({ json: finishCard(prepared, validatePages(pages, 'Commenti scheda ' + prepared.sourcePayload.id)) }));`),
  code('Schede con commenti conservati', [1920, 240], 'return $input.all().map(item => ({ json: finishCard(item.json) }));'),
  node('Aggiorna solo archivio Trello', 'n8n-nodes-base.postgres', 2.5, [2400, 100], {
    operation: 'executeQuery', query: fs.readFileSync(path.join(directory, 'upsert.sql'), 'utf8'),
    options: { queryReplacement: '={{ [JSON.stringify($json)] }}', queryBatching: 'transaction' },
  }, { retryOnFail: true, maxTries: 3, waitBetweenTries: 5000 }),
  node('Istruzioni', 'n8n-nodes-base.stickyNote', 1, [200, -500], {
    content: '## EdilPaint — Trello → NeonDB\n**Nessuna associazione automatica.**\n\n1. Creare TrelloCards con schema.sql oppure avviare il programma aggiornato.\n2. Selezionare la credenziale Trello nei 3 nodi HTTP.\n3. Selezionare la credenziale PostgreSQL nei 2 nodi database.\n4. Controllare gli ID nel nodo Configurazione.\n5. Eseguire manualmente, confrontare conteggi e solo poi pubblicare/attivare.\n\nLegge anche schede archiviate. Non scarica file. Non scrive su Trello, clienti o preventivi.\nCommenti nuovi/modificati riletti subito; controllo completo ogni24h. Errori interrompono il ramo senza cancellazioni.\nDocumentazione: docs/trello-preventivi.md',
    height: 430, width: 680,
  }),
];
const connections = {};
const connect = (from, to, output = 0) => {
  connections[from] ??= { main: [] };
  connections[from].main[output] ??= [];
  connections[from].main[output].push({ node: to, type: 'main', index: 0 });
};
for (const [from, to] of [
  ['Avvio manuale', 'Configurazione'], ['Ogni 15 minuti', 'Configurazione'], ['Configurazione', 'Leggi copia esistente'],
  ['Leggi copia esistente', 'Bacheche da leggere'], ['Bacheche da leggere', 'Leggi bacheche'], ['Leggi bacheche', 'Leggi tutte le schede'],
  ['Leggi tutte le schede', 'Prepara schede'], ['Prepara schede', 'Rileggere commenti'], ['Rileggere commenti', 'Leggi commenti completi'],
  ['Leggi commenti completi', 'Schede con commenti aggiornati'], ['Schede con commenti aggiornati', 'Aggiorna solo archivio Trello'],
  ['Schede con commenti conservati', 'Aggiorna solo archivio Trello'],
]) connect(from, to);
connect('Rileggere commenti', 'Schede con commenti conservati', 1);
const workflow = {
  name: 'EdilPaint — Trello nel database (solo sincronizzazione)', nodes, connections, active: false,
  settings: { executionOrder: 'v1', timezone: 'Europe/Rome', executionTimeout: 1800, saveDataSuccessExecution: 'none', saveDataErrorExecution: 'all' },
  pinData: {}, tags: [],
};
fs.writeFileSync(path.join(directory, 'trello-to-neon.workflow.json'), JSON.stringify(workflow, null, 2) + '\n');
console.log(`Workflow generato: ${nodes.length} nodi; inattivo e senza credenziali.`);
