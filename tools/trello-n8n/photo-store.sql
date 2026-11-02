-- One bound JSON parameter. An obsolete worker cannot overwrite a replacement request.
WITH input AS (SELECT $1::jsonb AS data),
active AS (
    SELECT request."CardId"
    FROM public."TrelloPhotoRequests" AS request CROSS JOIN input
    WHERE request."CardId" = data->>'CardId' AND request."Status" = 'processing'
      AND request."RequestedAtUtc" = (data->>'RequestedAtUtc')::timestamptz
    FOR UPDATE OF request
),
failed AS (
    UPDATE public."TrelloPhotoRequests" AS request
    SET "Status" = 'failed', "CompletedAtUtc" = NULL, "Error" = input.data->>'Error'
    FROM input
    WHERE request."CardId" = input.data->>'CardId'
      AND request."RequestedAtUtc" = (input.data->>'RequestedAtUtc')::timestamptz
      AND request."Status" = 'processing' AND COALESCE(input.data->>'Error', '') <> ''
      AND request."CardId" IN (SELECT "CardId" FROM active)
    RETURNING request."CardId"
), stored AS (
    INSERT INTO public."TrelloPhotos" AS saved
        ("CardId", "AttachmentId", "FileName", "ContentType", "ContentBase64", "SourceUrl", "SyncedAtUtc")
    SELECT data->>'CardId', data->>'AttachmentId', data->>'FileName', data->>'ContentType',
        data->>'ContentBase64', data->>'SourceUrl', GREATEST(clock_timestamp(), (data->>'RequestedAtUtc')::timestamptz)
    FROM input
    WHERE data->>'Mode' = 'photo' AND COALESCE(data->>'Error', '') = ''
      AND data->>'CardId' IN (SELECT "CardId" FROM active)
      AND EXISTS (SELECT 1 FROM public."TrelloPhotoRequests" AS request
          WHERE request."CardId" = data->>'CardId' AND request."Status" = 'processing'
            AND request."RequestedAtUtc" = (data->>'RequestedAtUtc')::timestamptz
            AND request."ExpectedAttachmentIdsJson"::jsonb ? (data->>'AttachmentId'))
    ON CONFLICT ("CardId", "AttachmentId") DO UPDATE SET
        "FileName" = EXCLUDED."FileName", "ContentType" = EXCLUDED."ContentType",
        "ContentBase64" = EXCLUDED."ContentBase64", "SourceUrl" = EXCLUDED."SourceUrl", "SyncedAtUtc" = EXCLUDED."SyncedAtUtc"
    WHERE saved."SyncedAtUtc" <= EXCLUDED."SyncedAtUtc"
    RETURNING "CardId", "AttachmentId", "SyncedAtUtc"
), completed AS (
UPDATE public."TrelloPhotoRequests" AS request
SET "Status" = 'completed', "CompletedAtUtc" = now(), "Error" = ''
FROM input
WHERE request."CardId" = input.data->>'CardId' AND request."Status" = 'processing'
  AND request."CardId" IN (SELECT "CardId" FROM active)
  AND request."RequestedAtUtc" = (input.data->>'RequestedAtUtc')::timestamptz
  AND COALESCE(input.data->>'Error', '') = ''
  AND NOT EXISTS (
      SELECT 1 FROM jsonb_array_elements_text(request."ExpectedAttachmentIdsJson"::jsonb) AS expected(id)
      WHERE NOT EXISTS (
          SELECT 1 FROM public."TrelloPhotos" AS photo
          WHERE photo."CardId" = request."CardId" AND photo."AttachmentId" = expected.id AND photo."SyncedAtUtc" >= request."RequestedAtUtc"
          UNION ALL
          SELECT 1 FROM stored WHERE stored."CardId" = request."CardId" AND stored."AttachmentId" = expected.id AND stored."SyncedAtUtc" >= request."RequestedAtUtc"
      )
  )
RETURNING request."CardId", request."Status"
)
SELECT "CardId", "Status" FROM completed
UNION ALL SELECT "CardId", 'failed' AS "Status" FROM failed
UNION ALL SELECT "CardId", 'processing' AS "Status" FROM stored
    WHERE NOT EXISTS (SELECT 1 FROM completed);
