using System.Data;
using System.IO;
using Ameli.Api.Domain;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameli.Api.Application;

public sealed partial class SecurityService
{
    private void BackupAudit(string outcome, AppUser admin, Actor actor, RequestOrigin origin, BackupCheckView view)
    {
        var detail = $"source={view.Source}; db={view.DatabaseName}; last={view.LastBackupAtUtc:O}; windowHours={view.WindowHours}; location={view.Location}; fallbackReason={view.FallbackReason}; msg={view.Message}";
        var e = Audit("backup_daily_check", outcome, origin, admin, detail: detail);
        e.Module = "Auditoría";
        e.Entity = "backups";
        e.EntityId = view.DatabaseName;
        e.SubjectUserId = null;
        e.SessionId = actor.SessionId;
    }

    public async Task<BackupCheckView> VerifyDailyBackupAsync(Actor actor, BackupCheckRequest request, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync([SqlSecurityTransaction.User(actor.UserId)], ct);
        var admin = await RequireAdminAsync(actor, ct);
        ValidateInput(request);

        var windowHours = request.WindowHours ?? settings.BackupWindowHours;
        if (windowHours <= 0) windowHours = 30;

        var checkedAt = Now;
        var threshold = checkedAt.AddHours(-windowHours);
        var databaseName = db.Database.GetDbConnection().Database;
        string fallbackReason = "";

        var sql = await SqlBackupCheckAsync(databaseName, threshold, checkedAt, windowHours, ct);
        BackupCheckView view;

        if (sql is not null)
        {
            view = sql with { FallbackReason = "" };
        }
        else
        {
            fallbackReason = "No fue posible verificar en SQL Server (sin permisos, sin msdb o consulta no disponible).";
            view = await FolderBackupCheckAsync(databaseName, threshold, checkedAt, windowHours, fallbackReason, ct);
        }

        BackupAudit(view.IsHealthy ? "Exitoso" : "Advertencia", admin, actor, origin, view);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return view;
    }

    private async Task<BackupCheckView?> SqlBackupCheckAsync(string databaseName, DateTimeOffset threshold, DateTimeOffset checkedAt, int windowHours, CancellationToken ct)
    {
        try
        {
            await using var connection = new SqlConnection(db.Database.GetConnectionString());
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = @"
SELECT TOP (1)
    backup_finish_date,
    physical_device_name
FROM msdb.dbo.backupset bs
INNER JOIN msdb.dbo.backupmediafamily bmf ON bs.media_set_id = bmf.media_set_id
WHERE bs.database_name = @db
  AND bs.type = 'D'
ORDER BY bs.backup_finish_date DESC";
            command.Parameters.Add(new SqlParameter("@db", SqlDbType.NVarChar, 128) { Value = databaseName });
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return new BackupCheckView(false, "warning", "sqlserver", databaseName, checkedAt, null, windowHours,
                    "No hay respaldos completos registrados en SQL Server para esta base.", "msdb.dbo.backupset", "");
            }

            var finish = reader.IsDBNull(0) ? (DateTimeOffset?)null : new DateTimeOffset(reader.GetDateTime(0), TimeSpan.Zero);
            var physical = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var healthy = finish.HasValue && finish.Value >= threshold;
            return new BackupCheckView(healthy, healthy ? "ok" : "warning", "sqlserver", databaseName, checkedAt, finish, windowHours,
                healthy
                    ? "Respaldo diario verificado correctamente en SQL Server."
                    : "El último respaldo completo en SQL Server está fuera de la ventana esperada.",
                string.IsNullOrWhiteSpace(physical) ? "msdb.dbo.backupset" : physical,
                "");
        }
        catch
        {
            return null;
        }
    }

    private Task<BackupCheckView> FolderBackupCheckAsync(string databaseName, DateTimeOffset threshold, DateTimeOffset checkedAt, int windowHours, string fallbackReason, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = (settings.BackupLocalPath ?? "").Trim();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return Task.FromResult(new BackupCheckView(false, "warning", "folder", databaseName, checkedAt, null, windowHours,
                "No hay ruta local de respaldos configurada o no existe.", string.IsNullOrWhiteSpace(path) ? "(sin configurar)" : path, fallbackReason));
        }

        var files = Directory.EnumerateFiles(path, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f =>
            {
                var ext = Path.GetExtension(f);
                return ext.Equals(".bak", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".trn", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".zip", StringComparison.OrdinalIgnoreCase);
            })
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();

        var last = files.FirstOrDefault();
        if (last is null)
        {
            return Task.FromResult(new BackupCheckView(false, "warning", "folder", databaseName, checkedAt, null, windowHours,
                "No se encontraron archivos de respaldo en la carpeta local.", path, fallbackReason));
        }

        var lastAt = new DateTimeOffset(last.LastWriteTimeUtc, TimeSpan.Zero);
        var healthy = lastAt >= threshold;
        return Task.FromResult(new BackupCheckView(healthy, healthy ? "ok" : "warning", "folder", databaseName, checkedAt, lastAt, windowHours,
            healthy
                ? "Respaldo diario verificado mediante carpeta local."
                : "El archivo de respaldo más reciente en carpeta local está fuera de la ventana esperada.",
            last.FullName, fallbackReason));
    }
}
