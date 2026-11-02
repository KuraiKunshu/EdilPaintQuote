import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import { canonical, prepareCard, finishCard, validatePages, PAGE_SIZE, MAX_PAGES } from './sync-core.mjs';

const workflow = JSON.parse(fs.readFileSync(new URL('./trello-to-neon.workflow.json', import.meta.url)));
const byName = name => workflow.nodes.find(node => node.name === name);
const config = { syncStartedAtUtc: '2026-11-02T10:00:00.000Z', forceCommentsRefresh: false };
const board = { id: '68eb83dcac50df446b956713', name: 'Bacheca di prova', url: 'https://trello.com/b/test', closed: false,
  lists: [{ id: 'list-1', name: 'Programmati', closed: false }], customFields: [{ id: 'field-1', name: 'Telefono', type: 'text' }], members: [] };
const card = { id: 'card-1', idBoard: board.id, idList: 'list-1', name: 'Nessuna associazione', desc: "Test con virgole, apostrofi ' e $1; DROP TABLE Quotes;",
  url: 'https://trello.com/c/abcdefgh/scheda', closed: false, due: null, dateLastActivity: '2026-11-02T09:00:00Z',
  badges: { comments: 0 }, checklists: [{ id: 'check-1', name: 'Rilievo', checkItems: [{ id: 'item-1', name: 'Misure', state: 'incomplete' }] }],
  customFieldItems: [{ idCustomField: 'field-1', value: { text: '123456' } }], attachments: [{ id: 'file-1', url: 'https://trello.com/test', name: 'foto.jpg' }], labels: [] };

test('first import includes archived cards, checklist, custom fields and file references without associations', () => {
  const result = finishCard(prepareCard({ ...card, closed: true }, board, null, config));
  assert.equal(result.IsArchived, true);
  assert.equal(result.IsDeleted, false);
  assert.equal(result.Id, card.id);
  assert.equal(result.Description, card.desc);
  assert.equal(result.DueUtc, null);
  const payload = JSON.parse(result.PayloadJson);
  assert.deepEqual(payload.checklists, card.checklists);
  assert.deepEqual(payload.customFields, board.customFields);
  assert.deepEqual(payload.customFieldItems, card.customFieldItems);
  assert.deepEqual(payload.attachments, card.attachments);
  assert.equal(payload._sync.binariesDownloaded, false);
  assert.ok(!Object.keys(result).some(key => /Customer|Quote|Preventivo|Cliente/i.test(key)));
});

test('same card ID is retained on rename and list change, without guessing customer identity', () => {
  const movedBoard = { ...board, lists: [...board.lists, { id: 'list-2', name: 'Fatti' }] };
  const result = finishCard(prepareCard({ ...card, name: 'Nuovo nome', idList: 'list-2' }, movedBoard, null, config));
  assert.equal(result.Id, card.id);
  assert.equal(result.ListName, 'Fatti');
});

test('unchanged card reuses complete comments, changed card and daily refresh request them again', () => {
  const withComments = { ...card, badges: { comments: 1 } };
  const prepared = prepareCard(withComments, board, null, config);
  assert.equal(prepared.needsComments, true);
  const saved = finishCard(prepared, [{ id: 'comment-1', data: { text: 'Prova' } }]);
  const later = { ...config, syncStartedAtUtc: '2026-11-02T10:15:00.000Z' };
  const unchanged = prepareCard(withComments, board, saved, later);
  assert.equal(unchanged.needsComments, false);
  assert.equal(JSON.parse(finishCard(unchanged).PayloadJson).actions.length, 1);
  assert.equal(prepareCard({ ...withComments, desc: 'Nuovo rilievo' }, board, saved, later).needsComments, true);
  assert.equal(prepareCard(withComments, board, saved, { ...later, syncStartedAtUtc: '2026-11-03T10:15:00.000Z' }).needsComments, true);
  assert.equal(prepareCard(withComments, board, saved, { ...later, forceCommentsRefresh: true }).needsComments, true);
});

test('zero comment badge clears removed comments and incomplete cached payload is repaired', () => {
  const saved = finishCard(prepareCard({ ...card, badges: { comments: 1 } }, board, null, config), [{ id: 'comment-1' }]);
  assert.deepEqual(JSON.parse(finishCard(prepareCard(card, board, saved, config)).PayloadJson).actions, []);
  assert.equal(prepareCard({ ...card, badges: { comments: 1 } }, board, { PayloadJson: '{invalid' }, config).needsComments, true);
});

test('paging includes more than 1000 cards and comments; duplicates/truncation/error bodies fail closed', () => {
  const first = Array.from({ length: PAGE_SIZE }, (_, i) => ({ id: String(i) }));
  assert.equal(validatePages([first, [{ id: '1000' }]], 'test').length, 1001);
  assert.throws(() => validatePages([first, [first[0]]], 'test'), /pagina ripetuta/);
  assert.throws(() => validatePages([{ error: 'Forbidden' }], 'test'), /risposta non valida/);
  assert.throws(() => validatePages(Array.from({ length: MAX_PAGES }, (_, p) => Array.from({ length: PAGE_SIZE }, (_, i) => ({ id: `${p}-${i}` }))), 'test'), /limite di paginazione/);
});

test('malformed payload/date/URL cannot overwrite the mirror', () => {
  assert.throws(() => prepareCard({ ...card, idBoard: 'other' }, board, null, config));
  assert.throws(() => prepareCard({ ...card, checklists: null }, board, null, config));
  assert.throws(() => prepareCard({ ...card, checklists: [{ id: 'bad' }] }, board, null, config));
  assert.throws(() => prepareCard({ ...card, closed: undefined }, board, null, config));
  assert.throws(() => finishCard(prepareCard({ ...card, due: 'not-a-date' }, board, null, config)));
  assert.throws(() => finishCard(prepareCard({ ...card, url: 'https://trello.com.attacker.invalid/c/abcdefgh' }, board, null, config)));
});

test('repeat imports upsert one card and older executions cannot replace a newer snapshot', () => {
  const records = new Map();
  const upsert = value => {
    const saved = records.get(value.Id);
    if (!saved || (saved.SyncedAtUtc <= value.SyncedAtUtc && (saved.LastActivityUtc ?? '') <= (value.LastActivityUtc ?? ''))) records.set(value.Id, value);
  };
  const first = finishCard(prepareCard(card, board, null, config));
  const newer = finishCard(prepareCard({ ...card, name: 'Aggiornato', dateLastActivity: '2026-11-02T10:20:00Z' }, board, first, { ...config, syncStartedAtUtc: '2026-11-02T10:30:00Z' }));
  upsert(first); upsert(first); upsert(newer); upsert(first);
  assert.equal(records.size, 1);
  assert.equal(records.get(card.id).Name, 'Aggiornato');
});

test('workflow uses native read-only HTTP, credential selectors and bound SQL limited to TrelloCards', () => {
  assert.equal(workflow.active, false);
  assert.ok(workflow.nodes.some(node => node.type === 'n8n-nodes-base.manualTrigger'));
  assert.ok(workflow.nodes.some(node => node.type === 'n8n-nodes-base.scheduleTrigger'));
  for (const node of workflow.nodes) {
    assert.ok(!node.credentials, 'Export must not contain credential IDs or secrets');
    if (node.type === 'n8n-nodes-base.httpRequest') {
      assert.equal(node.parameters.method, 'GET');
      assert.equal(node.parameters.nodeCredentialType, 'trelloApi');
      assert.ok(node.parameters.url.includes('https://api.trello.com/1/'));
      assert.ok(node.retryOnFail);
    }
    if (node.type === 'n8n-nodes-base.postgres') {
      assert.match(node.parameters.query, /public\."TrelloCards"/);
      assert.doesNotMatch(node.parameters.query, /(?:INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+(?:public\.)?"?(?:Customers|Quotes)"?/i);
      assert.match(node.parameters.options.queryReplacement, /^=\{\{ \[/);
      assert.doesNotMatch(node.parameters.query, /\{\{/);
    }
    if (node.type === 'n8n-nodes-base.code') {
      assert.doesNotMatch(node.parameters.jsCode, /httpRequest|fetch\(|require\(|\$env|CustomerSyncId|TrelloCardId/);
      new vm.Script('(function(){' + node.parameters.jsCode + '\n})');
    }
  }
  assert.match(byName('Leggi tutte le schede').parameters.url, /cards\/all/);
  assert.equal(byName('Leggi tutte le schede').parameters.queryParameters.parameters.find(value => value.name === 'sort').value, '-id');
  assert.match(byName('Aggiorna solo archivio Trello').parameters.query, /saved\."SyncedAtUtc" <= EXCLUDED\."SyncedAtUtc"/);
});

test('generated card Code node preserves pairing across two boards and multiple pages', () => {
  const board2 = { ...board, id: 'board-2', name: 'Seconda bacheca' };
  const pages = [{ json: { body: [card] } }, { json: { body: [{ ...card, id: 'card-2', idBoard: board2.id }] } }];
  const context = {
    $input: { all: () => pages },
    $: name => ({ first: () => ({ json: name === 'Configurazione' ? config : { existing: [] } }), itemMatching: index => ({ json: index === 0 ? board : board2 }) }),
  };
  const output = vm.runInNewContext('(function(){' + byName('Prepara schede').parameters.jsCode + '\n})()', context);
  assert.equal(output.length, 2);
  assert.equal(output[0].json.sourcePayload.board.name, board.name);
  assert.equal(output[1].json.sourcePayload.board.name, board2.name);
});

test('canonical source comparison ignores JSON object key order', () => {
  assert.equal(canonical({ b: 1, a: { y: 2, x: 3 } }), canonical({ a: { x: 3, y: 2 }, b: 1 }));
});
