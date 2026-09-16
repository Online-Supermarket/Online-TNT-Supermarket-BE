using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TNT.ProductService.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateCategoryProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Categories' AND column_name='ImageUrl') THEN
                        ALTER TABLE ""Categories"" DROP COLUMN ""ImageUrl"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Categories' AND column_name='UpdatedAtUtc') THEN
                        ALTER TABLE ""Categories"" RENAME COLUMN ""UpdatedAtUtc"" TO ""UpdatedAt"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Categories' AND column_name='CreatedAtUtc') THEN
                        ALTER TABLE ""Categories"" RENAME COLUMN ""CreatedAtUtc"" TO ""CreatedAt"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Categories' AND column_name='UpdatedAt') THEN
                        ALTER TABLE ""Categories"" ALTER COLUMN ""UpdatedAt"" DROP NOT NULL;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "Categories",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "Categories",
                newName: "CreatedAtUtc");

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Categories",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
