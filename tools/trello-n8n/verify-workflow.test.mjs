import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const workflow = JSON.parse(fs.readFileSync(new URL('./verify-workflow.json', import.meta.url)));
const collector = workflow.nodes.find(node => node.type === 'n8n-nodes-base.code').parameters.jsCode;
const run = pages => vm.runInNewContext('(function(){' + collector + '\n})()', { $input: { all: () => pages.map(body => ({ json: { body } })) } });
const card = index => ({ id: String(index), closed: index % 2 === 0, badges: { checkItems: 3, attachments: 2, comments: 1 } });

test('manual verifier has four nodes, read-only HTTP and a bound SELECT query', () => {
  assert.equal(workflow.active, false);
  assert.equal(workflow.nodes.length, 4);
  assert.equal(workflow.nodes.filter(node => node.type === 'n8n-nodes-base.manualTrigger').length, 1);
  assert.ok(!workflow.nodes.some(node => node.type.includes('schedule')));
  for (const node of workflow.nodes) assert.ok(!node.credentials);
  const http = workflow.nodes.find(node => node.type === 'n8n-nodes-base.httpRequest');
  assert.equal(http.parameters.method, 'GET');
  assert.equal(http.parameters.queryParameters.parameters.find(value => value.name === 'fields').value, 'id,closed,badges');
  const database = workflow.nodes.find(node => node.type === 'n8n-nodes-base.postgres');
  assert.match(database.parameters.query, /FROM public\."TrelloCards"/);
  assert.match(database.parameters.query, /FROM public\."Quotes"/);
  assert.doesNotMatch(database.parameters.query, /\b(?:INSERT|UPDATE|DELETE|ALTER|DROP|CREATE|TRUNCATE|MERGE)\b/i);
  assert.doesNotMatch(database.parameters.query, /\{\{/);
  assert.equal(database.parameters.options.queryReplacement, '={{ [JSON.stringify($json)] }}');
});

test('collects more than 1000 cards and counters without copying titles, descriptions or files', () => {
  const result = run([Array.from({ length: 1000 }, (_, index) => card(index)), [card(1000)]])[0].json;
  assert.equal(result.sourceCardCount, 1001);
  assert.equal(result.sourceArchivedCount, 501);
  assert.equal(result.sourceActiveCount, 500);
  assert.equal(result.sourceCheckItems, 3003);
  assert.equal(result.sourceAttachments, 2002);
  assert.equal(result.sourceComments, 1001);
  assert.equal(result.cards.length, 1001);
  assert.ok(!result.cards.some(item => 'desc' in item || 'name' in item || 'attachments' in item));
});

test('empty board yields zero counts and still reaches database SELECT', () => {
  const result = run([[]])[0].json;
  assert.equal(result.sourceCardCount, 0);
  assert.equal(result.cards.length, 0);
});

test('duplicate, failed and incomplete responses cannot produce a reassuring partial result', () => {
  assert.throws(() => run([[card(1)], [card(1)]]), /Pagina ripetuta/);
  assert.throws(() => run([{ error: 'Forbidden' }]), /non ha restituito/);
  assert.throws(() => run([[{ ...card(1), badges: {} }]]), /Contatore Trello/);
});
