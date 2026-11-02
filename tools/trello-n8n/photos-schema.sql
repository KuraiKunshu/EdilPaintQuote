-- Derived photograph cache and an authenticated database queue. No automatic association.
CREATE TABLE IF NOT EXISTS public."TrelloPhotos" (
    "CardId" varchar(64) NOT NULL,
    "AttachmentId" varchar(64) NOT NULL,
    "FileName" text NOT NULL DEFAULT '',
    "ContentType" varchar(100) NOT NULL DEFAULT '',
    "ContentBase64" text NOT NULL DEFAULT '',
    "SourceUrl" text NOT NULL DEFAULT '',
    "SyncedAtUtc" timestamptz NOT NULL,
    PRIMARY KEY ("CardId", "AttachmentId")
);
CREATE TABLE IF NOT EXISTS public."TrelloPhotoRequests" (
    "CardId" varchar(64) PRIMARY KEY,
    "RequestedAtUtc" timestamptz NOT NULL,
    "StartedAtUtc" timestamptz NULL,
    "CompletedAtUtc" timestamptz NULL,
    "Status" varchar(20) NOT NULL DEFAULT 'pending',
    "Error" text NOT NULL DEFAULT '',
    "ExpectedAttachmentIdsJson" text NOT NULL DEFAULT '[]'
);
CREATE INDEX IF NOT EXISTS "IX_TrelloPhotoRequests_Status" ON public."TrelloPhotoRequests" ("Status");
