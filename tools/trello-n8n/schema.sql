-- Bootstrap of the mirror only. No customer, quote or association is changed.
CREATE TABLE IF NOT EXISTS public."TrelloCards" (
    "Id" varchar(64) PRIMARY KEY,
    "BoardId" varchar(64) NOT NULL,
    "BoardName" text NOT NULL DEFAULT '',
    "ListId" varchar(64) NOT NULL DEFAULT '',
    "ListName" text NOT NULL DEFAULT '',
    "Name" text NOT NULL DEFAULT '',
    "Description" text NOT NULL DEFAULT '',
    "Url" text NOT NULL DEFAULT '',
    "IsArchived" boolean NOT NULL DEFAULT false,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    "DueUtc" timestamptz NULL,
    "LastActivityUtc" timestamptz NULL,
    "SyncedAtUtc" timestamptz NOT NULL,
    "PayloadJson" text NOT NULL DEFAULT '{}'
);
CREATE INDEX IF NOT EXISTS "IX_TrelloCards_BoardId" ON public."TrelloCards" ("BoardId");
