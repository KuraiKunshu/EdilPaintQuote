-- Bind a single JSON document as $1. Card text is never interpolated into SQL.
WITH incoming AS (SELECT $1::jsonb AS data)
INSERT INTO public."TrelloCards" AS saved (
    "Id", "BoardId", "BoardName", "ListId", "ListName", "Name", "Description", "Url",
    "IsArchived", "IsDeleted", "DueUtc", "LastActivityUtc", "SyncedAtUtc", "PayloadJson"
)
SELECT
    data->>'Id', data->>'BoardId', data->>'BoardName', data->>'ListId', data->>'ListName',
    data->>'Name', data->>'Description', data->>'Url',
    (data->>'IsArchived')::boolean, false,
    (data->>'DueUtc')::timestamptz, (data->>'LastActivityUtc')::timestamptz,
    (data->>'SyncedAtUtc')::timestamptz, data->>'PayloadJson'
FROM incoming
ON CONFLICT ("Id") DO UPDATE SET
    "BoardId" = EXCLUDED."BoardId", "BoardName" = EXCLUDED."BoardName",
    "ListId" = EXCLUDED."ListId", "ListName" = EXCLUDED."ListName",
    "Name" = EXCLUDED."Name", "Description" = EXCLUDED."Description", "Url" = EXCLUDED."Url",
    "IsArchived" = EXCLUDED."IsArchived", "IsDeleted" = EXCLUDED."IsDeleted",
    "DueUtc" = EXCLUDED."DueUtc", "LastActivityUtc" = EXCLUDED."LastActivityUtc",
    "SyncedAtUtc" = EXCLUDED."SyncedAtUtc", "PayloadJson" = EXCLUDED."PayloadJson"
WHERE saved."SyncedAtUtc" <= EXCLUDED."SyncedAtUtc"
  AND COALESCE(saved."LastActivityUtc", '-infinity'::timestamptz)
      <= COALESCE(EXCLUDED."LastActivityUtc", '-infinity'::timestamptz)
RETURNING "Id", "SyncedAtUtc";
