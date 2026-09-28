using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportCRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgentWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDone = table.Column<bool>(type: "bit", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsReminder = table.Column<bool>(type: "bit", nullable: false),
                    ReminderSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentTasks_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AgentTasks_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "QuickReplies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TitleAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TitleEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BodyAr = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    BodyEn = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Shortcut = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuickReplies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TicketMentions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketCommentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MentionedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MentionedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MentionedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketMentions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketMentions_TicketComments_TicketCommentId",
                        column: x => x.TicketCommentId,
                        principalTable: "TicketComments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TicketMentions_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_CustomerId",
                table: "AgentTasks",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_IsReminder_ReminderSentAt_DueAt",
                table: "AgentTasks",
                columns: new[] { "IsReminder", "ReminderSentAt", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_OwnerUserId_IsDone_DueAt",
                table: "AgentTasks",
                columns: new[] { "OwnerUserId", "IsDone", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_TicketId",
                table: "AgentTasks",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_QuickReplies_IsActive_DepartmentId",
                table: "QuickReplies",
                columns: new[] { "IsActive", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuickReplies_Shortcut",
                table: "QuickReplies",
                column: "Shortcut",
                unique: true,
                filter: "[Shortcut] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TicketMentions_MentionedUserId_MentionedAt",
                table: "TicketMentions",
                columns: new[] { "MentionedUserId", "MentionedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketMentions_TicketCommentId_MentionedUserId",
                table: "TicketMentions",
                columns: new[] { "TicketCommentId", "MentionedUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TicketMentions_TicketId",
                table: "TicketMentions",
                column: "TicketId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentTasks");

            migrationBuilder.DropTable(
                name: "QuickReplies");

            migrationBuilder.DropTable(
                name: "TicketMentions");
        }
    }
}
