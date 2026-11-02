import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as core from './photo-core.mjs';

const directory = path.dirname(fileURLToPath(import.meta.url));
const shared = `const MAX_PHOTO_BYTES = ${core.MAX_PHOTO_BYTES};\nconst MAX_PHOTOS_PER_REQUEST = ${core.MAX_PHOTOS_PER_REQUEST};\nconst MAX_REQUEST_BYTES = ${core.MAX_REQUEST_BYTES};\n` +
  ['photoError', 'preparePhotoRequest', 'classifyPhotoResponse', 'imageMime', 'encodePhoto'].map(name => core[name].toString()).join('\n');
const makeNode = (name, type, typeVersion, position, parameters, extra = {}) => ({
  id: name.toLowerCase().replace(/[^a-z0-9]+/g, '-'), name, type: 'n8n-nodes-base.' + type, typeVersion, position, parameters, ...extra,
});
const code = (name, position, jsCode) => makeNode(name, 'code', 2, position, { mode: 'runOnceForAllItems', jsCode: shared + '\n' + jsCode });
const condition = (name, position, value, type = 'boolean', operation = 'true') => makeNode(name, 'if', 2.2, position, {
  conditions: { options: { caseSensitive: true, leftValue: '', typeValidation: 'strict', version: 2 },
    conditions: [{ id: name, leftValue: value, rightValue: '', operator: { type, operation, singleValue: true } }], combinator: 'and' }, options: {},
});
const downloadOptions = {
  timeout: 120000, batching: { batch: { batchSize: 1, batchInterval: 500 } },
  // Never forward the Trello Authorization header to object storage.
  redirect: { redirect: { followRedirects: false, maxRedirects: 0 } },
  sendCredentialsOnCrossOriginRedirect: false,
  response: { response: { responseFormat: 'file', fullResponse: true, neverError: true, outputPropertyName: 'data' } },
};
const transferCode = (metadataNode) => `
const result = [];
for (const [index, item] of $input.all().entries()) {
  const photo = $('${metadataNode}').itemMatching(index).json;
  try {
    const bytes = item.binary?.data ? await this.helpers.getBinaryDataBuffer(index, 'data') : null;
    result.push({ json: encodePhoto(photo, item.json, bytes, new Date().toISOString()), pairedItem: { item: index } });
  } catch {
    result.push({ json: photoError(photo, 'Impossibile leggere la fotografia scaricata.'), pairedItem: { item: index } });
  }
}
return result;`;

const nodes = [
  makeNode('Avvio manuale', 'manualTrigger', 1, [0, 0], {}),
  makeNode('Ogni minuto', 'scheduleTrigger', 1.2, [0, 160], { rule: { interval: [{ field: 'minutes', minutesInterval: 1 }] } }),
  makeNode('Prendi una richiesta', 'postgres', 2.5, [240, 80], {
    operation: 'executeQuery', query: fs.readFileSync(path.join(directory, 'photo-claim.sql'), 'utf8'), options: {},
  }, { retryOnFail: true, maxTries: 3, waitBetweenTries: 5000 }),
  code('Prepara fotografie richieste', [480, 80], `
return $input.all().flatMap((item,index) => preparePhotoRequest(item.json).map(photo => ({ json: photo, pairedItem: { item: index } })));`),
  condition('Ci sono fotografie', [720, 80], '={{ $json.Mode === "photo" }}'),
  makeNode('Scarica da Trello con autorizzazione', 'httpRequest', 4.2, [960, -60], {
    method: 'GET', url: '={{ $json.SourceUrl }}', authentication: 'genericCredentialType', genericAuthType: 'httpHeaderAuth',
    options: downloadOptions,
  }, { retryOnFail: true, maxTries: 3, waitBetweenTries: 5000 }),
  code('Controlla risposta Trello', [1200, -60], `
return $input.all().map((item,index) => ({ json: classifyPhotoResponse($('Ci sono fotografie').itemMatching(index).json, item.json, Boolean(item.binary?.data)),
  ...(item.binary ? { binary: item.binary } : {}), pairedItem: { item: index } }));`),
  condition('Serve download firmato', [1440, -60], '={{ Boolean($json.SignedUrl) }}'),
  makeNode('Scarica fotografia senza credenziali', 'httpRequest', 4.2, [1680, -180], {
    method: 'GET', url: '={{ $json.SignedUrl }}', authentication: 'none', options: downloadOptions,
  }, { retryOnFail: true, maxTries: 3, waitBetweenTries: 5000 }),
  code('Verifica fotografia da storage', [1920, -180], transferCode('Serve download firmato')),
  code('Verifica fotografia diretta', [1680, 80], transferCode('Serve download firmato')),
  makeNode('Salva foto e stato richiesta', 'postgres', 2.5, [2160, 80], {
    operation: 'executeQuery', query: fs.readFileSync(path.join(directory, 'photo-store.sql'), 'utf8'),
    options: { queryReplacement: '={{ [JSON.stringify($json)] }}', queryBatching: 'transaction' },
  }, { retryOnFail: true, maxTries: 3, waitBetweenTries: 5000 }),
  makeNode('Istruzioni fotografie', 'stickyNote', 1, [240, -560], {
    content: '## Fotografie della scheda lavoro\nUna richiesta nasce solo dalla scheda Trello scelta manualmente nel preventivo.\n\n1. Applicare photos-schema.sql.\n2. Scegliere Postgres nei due nodi database.\n3. Nel solo nodo Scarica da Trello con autorizzazione scegliere la credenziale Header Auth per Trello.\n4. Nome header: Authorization. Valore OAuth con API key e token Trello, inseriti direttamente nella credenziale n8n. Nessun segreto nel workflow.\n5. Eseguire la prova con una richiesta reale, poi pubblicare.\n\nIl download autenticato non segue redirect: la fotografia eventualmente firmata viene scaricata senza credenziali.\nNon modifica Trello, clienti, preventivi o associazioni. Non scarica tutto l’archivio.\nLe richieste incomplete falliscono: nessuna stampa con foto mancanti viene dichiarata completa.',
    width: 780, height: 470,
  }),
];
const connections = {};
const connect = (from, to, output = 0) => {
  connections[from] ??= { main: [] }; connections[from].main[output] ??= [];
  connections[from].main[output].push({ node: to, type: 'main', index: 0 });
};
for (const [from, to] of [
  ['Avvio manuale', 'Prendi una richiesta'], ['Ogni minuto', 'Prendi una richiesta'],
  ['Prendi una richiesta', 'Prepara fotografie richieste'], ['Prepara fotografie richieste', 'Ci sono fotografie'],
  ['Ci sono fotografie', 'Scarica da Trello con autorizzazione'], ['Scarica da Trello con autorizzazione', 'Controlla risposta Trello'],
  ['Controlla risposta Trello', 'Serve download firmato'], ['Serve download firmato', 'Scarica fotografia senza credenziali'],
  ['Scarica fotografia senza credenziali', 'Verifica fotografia da storage'], ['Verifica fotografia da storage', 'Salva foto e stato richiesta'],
  ['Verifica fotografia diretta', 'Salva foto e stato richiesta'],
]) connect(from, to);
connect('Ci sono fotografie', 'Salva foto e stato richiesta', 1);
connect('Serve download firmato', 'Verifica fotografia diretta', 1);
const workflow = {
  name: 'EdilPaint — Fotografie Trello su richiesta', nodes, connections, active: false,
  settings: { executionOrder: 'v1', timezone: 'Europe/Rome', executionTimeout: 900,
    saveDataSuccessExecution: 'none', saveDataErrorExecution: 'none', saveManualExecutions: false },
  pinData: {}, tags: [],
};
fs.writeFileSync(path.join(directory, 'photo-workflow.json'), JSON.stringify(workflow, null, 2) + '\n');
console.log('Worker fotografie generato: inattivo; 2 credenziali Postgres e 1 Header Auth da selezionare.');
