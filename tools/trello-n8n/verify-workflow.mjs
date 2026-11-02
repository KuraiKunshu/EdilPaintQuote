import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const directory = path.dirname(fileURLToPath(import.meta.url));
const boardId = '68eb83dcac50df446b956713';
const query = `
WITH input AS (SELECT $1::jsonb AS data),
source AS (
    SELECT card->>'id' AS id, (card->>'closed')::boolean AS archived,
           (card->'badges'->>'checkItems')::integer AS check_items,
           (card->'badges'->>'attachments')::integer AS attachments,
           (card->'badges'->>'comments')::integer AS comments
    FROM input CROSS JOIN LATERAL jsonb_array_elements(data->'cards') AS card
), mirror AS (
    SELECT "Id" AS id, "IsArchived" AS archived, "IsDeleted" AS deleted,
           "SyncedAtUtc" AS synced_at, "PayloadJson"::jsonb AS payload
    FROM public."TrelloCards"
    WHERE "BoardId" = (SELECT data->>'boardId' FROM input)
), mirror_counts AS (
    SELECT mirror.*,
           (SELECT count(*) FROM jsonb_array_elements(COALESCE(payload->'checklists', '[]'::jsonb)) AS checklist
                CROSS JOIN LATERAL jsonb_array_elements(COALESCE(checklist->'checkItems', '[]'::jsonb))) AS check_items,
           jsonb_array_length(COALESCE(payload->'attachments', '[]'::jsonb)) AS attachments,
           jsonb_array_length(COALESCE(payload->'actions', '[]'::jsonb)) AS comments
    FROM mirror
), comparison AS (
    SELECT source.id AS source_id, mirror_counts.id AS mirror_id,
           source.archived AS source_archived, mirror_counts.archived AS mirror_archived,
           mirror_counts.deleted AS mirror_deleted,
           source.check_items AS source_check_items, mirror_counts.check_items AS mirror_check_items,
           source.attachments AS source_attachments, mirror_counts.attachments AS mirror_attachments,
           source.comments AS source_comments, mirror_counts.comments AS mirror_comments
    FROM source FULL OUTER JOIN mirror_counts ON source.id = mirror_counts.id
)
SELECT
    (SELECT data->>'boardId' FROM input) AS "BoardId",
    (SELECT count(*) FROM source) AS "SourceCards",
    (SELECT count(*) FROM source WHERE NOT archived) AS "SourceActive",
    (SELECT count(*) FROM source WHERE archived) AS "SourceArchived",
    (SELECT count(*) FROM mirror) AS "DatabaseCards",
    (SELECT count(*) FROM mirror WHERE NOT archived AND NOT deleted) AS "DatabaseActive",
    (SELECT count(*) FROM mirror WHERE archived) AS "DatabaseArchived",
    (SELECT COALESCE(sum(check_items),0) FROM source) AS "SourceCheckItems",
    (SELECT COALESCE(sum(check_items),0) FROM mirror_counts) AS "DatabaseCheckItems",
    (SELECT COALESCE(sum(attachments),0) FROM source) AS "SourceAttachments",
    (SELECT COALESCE(sum(attachments),0) FROM mirror_counts) AS "DatabaseAttachments",
    (SELECT COALESCE(sum(comments),0) FROM source) AS "SourceComments",
    (SELECT COALESCE(sum(comments),0) FROM mirror_counts) AS "DatabaseComments",
    count(*) FILTER (WHERE source_id IS NOT NULL AND mirror_id IS NULL) AS "MissingCards",
    COALESCE(jsonb_agg(source_id) FILTER (WHERE source_id IS NOT NULL AND mirror_id IS NULL),'[]'::jsonb) AS "MissingCardIds",
    count(*) FILTER (WHERE source_id IS NULL AND mirror_id IS NOT NULL) AS "ExtraCards",
    COALESCE(jsonb_agg(mirror_id) FILTER (WHERE source_id IS NULL AND mirror_id IS NOT NULL),'[]'::jsonb) AS "ExtraCardIds",
    count(*) FILTER (WHERE source_id IS NOT NULL AND mirror_id IS NOT NULL AND (source_archived IS DISTINCT FROM mirror_archived OR mirror_deleted)) AS "ArchiveMismatches",
    count(*) FILTER (WHERE source_id IS NOT NULL AND mirror_id IS NOT NULL AND source_check_items IS DISTINCT FROM mirror_check_items) AS "ChecklistMismatches",
    count(*) FILTER (WHERE source_id IS NOT NULL AND mirror_id IS NOT NULL AND source_attachments IS DISTINCT FROM mirror_attachments) AS "AttachmentMismatches",
    count(*) FILTER (WHERE source_id IS NOT NULL AND mirror_id IS NOT NULL AND source_comments IS DISTINCT FROM mirror_comments) AS "CommentMismatches",
    COALESCE(jsonb_agg(jsonb_build_object('Id',source_id,'SourceCheckItems',source_check_items,'DatabaseCheckItems',mirror_check_items,'SourceAttachments',source_attachments,'DatabaseAttachments',mirror_attachments,'SourceComments',source_comments,'DatabaseComments',mirror_comments,'SourceArchived',source_archived,'DatabaseArchived',mirror_archived))
        FILTER (WHERE source_id IS NOT NULL AND mirror_id IS NOT NULL AND (source_archived IS DISTINCT FROM mirror_archived OR mirror_deleted OR source_check_items IS DISTINCT FROM mirror_check_items OR source_attachments IS DISTINCT FROM mirror_attachments OR source_comments IS DISTINCT FROM mirror_comments)), '[]'::jsonb) AS "Mismatches",
    (SELECT count(*) FROM public."Quotes" WHERE COALESCE(btrim("TrelloCardId"),'') <> '') AS "QuotesWithManualTrelloAssociation",
    (SELECT min(synced_at) FROM mirror) AS "OldestSyncUtc",
    (SELECT max(synced_at) FROM mirror) AS "NewestSyncUtc",
    now() AS "VerifiedAtUtc"
FROM comparison;
`.trim();

const collectCode = `
const PAGE_SIZE = 1000;
const MAX_PAGES = 200;
const pages = $input.all().map(item => item.json.body);
const cards = [];
const seen = new Set();
for (const page of pages) {
  if (!Array.isArray(page)) throw new Error('Trello non ha restituito un elenco di schede: verifica interrotta.');
  for (const card of page) {
    if (!card || typeof card.id !== 'string' || typeof card.closed !== 'boolean' || !card.badges) throw new Error('Scheda o contatori Trello incompleti.');
    if (seen.has(card.id)) throw new Error('Pagina ripetuta o schede cambiate durante la lettura: ripetere la verifica.');
    for (const key of ['checkItems','attachments','comments']) {
      if (!Number.isInteger(card.badges[key]) || card.badges[key] < 0) throw new Error('Contatore Trello mancante o non valido: ' + key);
    }
    seen.add(card.id);
    cards.push({ id: card.id, closed: card.closed, badges: { checkItems: card.badges.checkItems, attachments: card.badges.attachments, comments: card.badges.comments } });
  }
}
if (pages.length >= MAX_PAGES && pages.at(-1)?.length === PAGE_SIZE) throw new Error('Limite paginazione raggiunto: aumentare il limite prima della verifica.');
return [{ json: { boardId: '${boardId}', cards, sourceCardCount: cards.length,
  sourceActiveCount: cards.filter(card => !card.closed).length,
  sourceArchivedCount: cards.filter(card => card.closed).length,
  sourceCheckItems: cards.reduce((sum,card) => sum + card.badges.checkItems, 0),
  sourceAttachments: cards.reduce((sum,card) => sum + card.badges.attachments, 0),
  sourceComments: cards.reduce((sum,card) => sum + card.badges.comments, 0) } }];
`.trim();

const workflow = {
  name: 'EdilPaint — Verifica Trello e database', active: false,
  nodes: [
    { id: 'verify-start', name: 'Avvio manuale', type: 'n8n-nodes-base.manualTrigger', typeVersion: 1, position: [0, 0], parameters: {} },
    { id: 'verify-trello', name: 'Leggi contatori Trello', type: 'n8n-nodes-base.httpRequest', typeVersion: 4.2, position: [260, 0],
      retryOnFail: true, maxTries: 5, waitBetweenTries: 5000,
      parameters: {
        method: 'GET', url: `https://api.trello.com/1/boards/${boardId}/cards/all`, authentication: 'predefinedCredentialType', nodeCredentialType: 'trelloApi',
        sendQuery: true, queryParameters: { parameters: [{ name: 'fields', value: 'id,closed,badges' }, { name: 'limit', value: '1000' }, { name: 'sort', value: '-id' }] },
        options: { timeout: 120000, response: { response: { responseFormat: 'json', fullResponse: true, neverError: false } },
          pagination: { pagination: {
            paginationMode: 'updateAParameterInEachRequest',
            parameters: { parameters: [{ type: 'qs', name: 'before', value: "={{ $pageCount === 0 ? $now.toISO() : $response.body[$response.body.length - 1].id }}" }] },
            paginationCompleteWhen: 'other', completeExpression: '={{ Array.isArray($response.body) && $response.body.length < 1000 }}',
            limitPagesFetched: true, maxRequests: 200, requestInterval: 500,
          } },
        },
      },
    },
    { id: 'verify-source', name: 'Riepilogo sorgente', type: 'n8n-nodes-base.code', typeVersion: 2, position: [520, 0], parameters: { mode: 'runOnceForAllItems', jsCode: collectCode } },
    { id: 'verify-database', name: 'Confronta senza modificare', type: 'n8n-nodes-base.postgres', typeVersion: 2.5, position: [780, 0],
      retryOnFail: true, maxTries: 3, waitBetweenTries: 5000,
      parameters: { operation: 'executeQuery', query, options: { queryReplacement: '={{ [JSON.stringify($json)] }}' } },
    },
  ],
  connections: {
    'Avvio manuale': { main: [[{ node: 'Leggi contatori Trello', type: 'main', index: 0 }]] },
    'Leggi contatori Trello': { main: [[{ node: 'Riepilogo sorgente', type: 'main', index: 0 }]] },
    'Riepilogo sorgente': { main: [[{ node: 'Confronta senza modificare', type: 'main', index: 0 }]] },
  },
  settings: { executionOrder: 'v1', timezone: 'Europe/Rome', executionTimeout: 300, saveDataSuccessExecution: 'all', saveDataErrorExecution: 'all' },
  pinData: {}, tags: [],
};
fs.writeFileSync(path.join(directory, 'verify-workflow.json'), JSON.stringify(workflow, null, 2) + '\n');
console.log('Verificatore generato: 4 nodi, soltanto GET Trello e SELECT database.');
