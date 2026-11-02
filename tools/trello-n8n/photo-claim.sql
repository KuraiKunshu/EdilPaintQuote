-- A worker claims one request. The program creates requests only after manual association.
WITH candidate AS (
    SELECT "CardId"
    FROM public."TrelloPhotoRequests"
    WHERE "Status" = 'pending'
       OR ("Status" = 'processing' AND ("StartedAtUtc" IS NULL OR "StartedAtUtc" < now() - interval '15 minutes'))
    ORDER BY "RequestedAtUtc"
    FOR UPDATE SKIP LOCKED
    LIMIT 1
)
UPDATE public."TrelloPhotoRequests" AS request
SET "Status" = 'processing', "StartedAtUtc" = now(), "CompletedAtUtc" = NULL, "Error" = ''
FROM candidate
WHERE request."CardId" = candidate."CardId"
RETURNING request."CardId", request."RequestedAtUtc"::text AS "RequestedAtUtc", request."ExpectedAttachmentIdsJson",
    (SELECT "PayloadJson" FROM public."TrelloCards" WHERE "Id" = request."CardId") AS "PayloadJson";
