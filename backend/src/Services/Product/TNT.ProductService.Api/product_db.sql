CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

CREATE TABLE "Categories" (
    "Id" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "Description" text,
    "ImageUrl" character varying(500),
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Categories" PRIMARY KEY ("Id")
);

CREATE TABLE "Products" (
    "Id" uuid NOT NULL,
    "CategoryId" uuid,
    "Name" character varying(200) NOT NULL,
    "Description" text,
    "Price" numeric(10,2) NOT NULL,
    "StockQuantity" integer NOT NULL DEFAULT 0,
    "Unit" character varying(50) NOT NULL DEFAULT 'item',
    "ImageUrl" character varying(500),
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_Products" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Products_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX "IX_Categories_Name" ON "Categories" ("Name");

CREATE INDEX "IX_Products_CategoryId" ON "Products" ("CategoryId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260914092706_AddProductCatalog', '8.0.11');

COMMIT;

START TRANSACTION;

ALTER TABLE "Categories" DROP COLUMN "ImageUrl";

ALTER TABLE "Categories" RENAME COLUMN "UpdatedAtUtc" TO "UpdatedAt";

ALTER TABLE "Categories" RENAME COLUMN "CreatedAtUtc" TO "CreatedAt";

ALTER TABLE "Categories" ALTER COLUMN "UpdatedAt" DROP NOT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260914214440_UpdateCategoryProperties', '8.0.11');

COMMIT;

