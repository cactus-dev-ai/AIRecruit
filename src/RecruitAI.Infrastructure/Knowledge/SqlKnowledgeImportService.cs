using System.Data;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Data.SqlClient;
using RecruitAI.Application.Common;
using RecruitAI.Application.Knowledge;
using RecruitAI.Infrastructure.Database;

namespace RecruitAI.Infrastructure.Knowledge;

public sealed class SqlKnowledgeImportService(
    SqlConnectionFactory connectionFactory,
    KnowledgeImportSettings settings) : IKnowledgeImportService
{
    private static readonly string[] Headers = ["知识编码", "标题", "文档类型", "内容"];
    private static readonly HashSet<string> Types = new(StringComparer.OrdinalIgnoreCase)
        { "JOB_REQUIREMENT", "COMPANY_INFO", "INDUSTRY_INFO", "TECH_STANDARD", "TALENT_STANDARD", "SCORE_RULE", "OTHER" };
    private static readonly Regex CodePattern = new("^[A-Z0-9_-]{1,100}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<long> ParseExcelAsync(Stream stream, string fileName, long fileLength, long userId, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("KNOWLEDGE_FILE_TYPE_INVALID", "只允许上传.xlsx文件。 ");
        if (fileLength <= 0 || fileLength > settings.MaxFileBytes)
            throw new BusinessException("KNOWLEDGE_FILE_SIZE_INVALID", "Excel文件必须大于0且不超过10MB。 ");

        await using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        ValidatePackageSafety(memory);
        memory.Position = 0;
        var hash = Convert.ToHexString(SHA256.HashData(memory));
        memory.Position = 0;
        var parsed = ParseRows(memory);
        if (parsed.Count > settings.MaxRows)
            throw new BusinessException("KNOWLEDGE_ROW_LIMIT", $"数据行不能超过{settings.MaxRows}行。 ");

        return await SaveParsedRowsAsync(SanitizeFileName(fileName), hash, parsed, userId, cancellationToken);
    }

    public async Task<KnowledgeImportBatchDto?> GetAsync(long batchId, CancellationToken cancellationToken)
    {
        const string batchSql = """
            SELECT Id,OriginalFileName,Status,TotalCount,ReadyCount,DuplicateCount,ConflictCount,InvalidCount,RowVersion
            FROM dbo.KnowledgeImportBatch WHERE Id=@Id
            """;
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        (long Id, string Name, string Status, int Total, int Ready, int Duplicate, int Conflict, int Invalid, string Version)? batch = null;
        await using (var command = new SqlCommand(batchSql, connection))
        {
            command.Parameters.AddWithValue("@Id", batchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                batch = (reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4),
                    reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), Convert.ToBase64String((byte[])reader[8]));
        }
        if (batch is null) return null;
        const string rowsSql = """
            SELECT Id,RowNumber,Code,Title,DocumentType,Content,Status,ExistingDocumentId,ErrorMessage,RowVersion
            FROM dbo.KnowledgeImportRow WHERE BatchId=@Id ORDER BY RowNumber
            """;
        var rows = new List<KnowledgeImportRowDto>();
        await using (var command = new SqlCommand(rowsSql, connection))
        {
            command.Parameters.AddWithValue("@Id", batchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) rows.Add(new(
                reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6), reader.GetNullableInt64(7), reader.GetNullableString(8),
                Convert.ToBase64String((byte[])reader[9])));
        }
        var b = batch.Value;
        return new(b.Id, b.Name, b.Status, b.Total, b.Ready, b.Duplicate, b.Conflict, b.Invalid, b.Version, rows);
    }

    public async Task UpdateRowAsync(long batchId, long rowId, UpdateKnowledgeImportRowRequest request, long userId, CancellationToken cancellationToken)
    {
        var rowVersion = ParseRowVersion(request.RowVersion);
        var normalized = Normalize(request.Code, request.Title, request.DocumentType, request.Content);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var classification = await ClassifyAsync(connection, null, normalized, cancellationToken);
        if (normalized.Error is null)
        {
            await using var duplicate = new SqlCommand("SELECT COUNT(*) FROM dbo.KnowledgeImportRow WHERE BatchId=@BatchId AND Id<>@RowId AND Code=@Code AND Status<>'IMPORTED'", connection);
            duplicate.Parameters.AddWithValue("@BatchId", batchId); duplicate.Parameters.AddWithValue("@RowId", rowId); duplicate.Parameters.Add("@Code", SqlDbType.NVarChar, 100).Value = normalized.Code;
            if (Convert.ToInt32(await duplicate.ExecuteScalarAsync(cancellationToken)) > 0)
                classification = new("INVALID", null, "同一Excel中知识编码重复。 ");
        }
        const string updateSql = """
            UPDATE dbo.KnowledgeImportRow SET Code=@Code,Title=@Title,DocumentType=@Type,Content=@Content,
              ContentHash=@Hash,Status=@Status,ExistingDocumentId=@ExistingId,ErrorMessage=@Error,
              UpdatedBy=@UserId,UpdateTime=SYSUTCDATETIME()
            WHERE Id=@RowId AND BatchId=@BatchId AND RowVersion=@RowVersion
              AND EXISTS (SELECT 1 FROM dbo.KnowledgeImportBatch b WHERE b.Id=@BatchId AND b.Status IN ('PARSED','READY','CONFLICT'))
            """;
        await using var command = new SqlCommand(updateSql, connection);
        AddRowParameters(command, normalized, classification);
        command.Parameters.AddWithValue("@UserId", userId); command.Parameters.AddWithValue("@RowId", rowId); command.Parameters.AddWithValue("@BatchId", batchId);
        command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = rowVersion;
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new BusinessException("CONCURRENCY_CONFLICT", "暂存行已被修改，请刷新。", 409);
        await RecountBatchAsync(connection, null, batchId, cancellationToken);
    }

    public async Task<int> ConfirmAsync(long batchId, long userId, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await RecountBatchAsync(connection, transaction, batchId, cancellationToken);
            await using (var check = new SqlCommand("SELECT ConflictCount+InvalidCount FROM dbo.KnowledgeImportBatch WITH (UPDLOCK,HOLDLOCK) WHERE Id=@Id AND Status IN ('READY','CONFLICT','PARSED')", connection, transaction))
            {
                check.Parameters.AddWithValue("@Id", batchId);
                var blocked = await check.ExecuteScalarAsync(cancellationToken);
                if (blocked is null) throw new BusinessException("KNOWLEDGE_BATCH_NOT_CONFIRMABLE", "知识导入批次不存在或已确认。", 409);
                if (Convert.ToInt32(blocked) > 0) throw new BusinessException("KNOWLEDGE_BATCH_HAS_CONFLICT", "仍有冲突或非法行，修正后才能确认。", 409);
            }
            var rows = new List<(long RowId, string Code, string Title, string Type, string Content, string Hash, long? ExistingId)>();
            await using (var select = new SqlCommand("SELECT Id,Code,Title,DocumentType,Content,ContentHash,ExistingDocumentId FROM dbo.KnowledgeImportRow WHERE BatchId=@Id AND Status='READY' ORDER BY RowNumber", connection, transaction))
            {
                select.Parameters.AddWithValue("@Id", batchId);
                await using var reader = await select.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetNullableInt64(6)));
            }
            foreach (var row in rows)
            {
                long documentId; int version;
                if (row.ExistingId is long existingId)
                {
                    await using var update = new SqlCommand("""
                        UPDATE dbo.KnowledgeDocument SET Code=@Code,Title=@Title,DocumentType=@Type,Content=@Content,
                          ContentHash=@Hash,Version=Version+1,Status='INDEXING',IsDeleted=0,UpdateTime=SYSUTCDATETIME()
                        WHERE Id=@Id; SELECT Version FROM dbo.KnowledgeDocument WHERE Id=@Id;
                        """, connection, transaction);
                    AddKnowledgeParameters(update, row.Code, row.Title, row.Type, row.Content, row.Hash); update.Parameters.AddWithValue("@Id", existingId);
                    version = Convert.ToInt32(await update.ExecuteScalarAsync(cancellationToken)); documentId = existingId;
                }
                else
                {
                    await using var insert = new SqlCommand("""
                        INSERT dbo.KnowledgeDocument(Code,Title,DocumentType,Content,Status,ContentHash,Version,CreatedBy,IsDeleted)
                        VALUES(@Code,@Title,@Type,@Content,'INDEXING',@Hash,1,@UserId,0);
                        SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
                        """, connection, transaction);
                    AddKnowledgeParameters(insert, row.Code, row.Title, row.Type, row.Content, row.Hash); insert.Parameters.AddWithValue("@UserId", userId);
                    documentId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken)); version = 1;
                }
                await using var task = new SqlCommand("""
                    IF NOT EXISTS (SELECT 1 FROM dbo.AITask WHERE BusinessKey=@BusinessKey)
                      INSERT dbo.AITask(TaskType,KnowledgeDocumentId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey)
                      VALUES('KNOWLEDGE_EMBED',@DocumentId,'WAITING',5,0,3,@BusinessKey)
                    """, connection, transaction);
                task.Parameters.AddWithValue("@DocumentId", documentId); task.Parameters.AddWithValue("@BusinessKey", $"knowledge-embed:{documentId}:{version}");
                await task.ExecuteNonQueryAsync(cancellationToken);
                await using var mark = new SqlCommand("UPDATE dbo.KnowledgeImportRow SET Status='IMPORTED',ExistingDocumentId=@DocumentId,UpdateTime=SYSUTCDATETIME(),UpdatedBy=@UserId WHERE Id=@RowId", connection, transaction);
                mark.Parameters.AddWithValue("@DocumentId", documentId); mark.Parameters.AddWithValue("@UserId", userId); mark.Parameters.AddWithValue("@RowId", row.RowId);
                await mark.ExecuteNonQueryAsync(cancellationToken);
            }
            await using var finish = new SqlCommand("UPDATE dbo.KnowledgeImportBatch SET Status='IMPORTED',ConfirmedBy=@UserId,ConfirmTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME() WHERE Id=@Id", connection, transaction);
            finish.Parameters.AddWithValue("@UserId", userId); finish.Parameters.AddWithValue("@Id", batchId); await finish.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken); return rows.Count;
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    private List<ParsedRow> ParseRows(Stream stream)
    {
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart ?? throw new BusinessException("KNOWLEDGE_WORKBOOK_INVALID", "Excel工作簿无效。 ");
        var sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>().ToList() ?? [];
        if (sheets.Count != 1 || !string.Equals(sheets[0].Name?.Value, settings.RequiredWorksheetName, StringComparison.Ordinal))
            throw new BusinessException("KNOWLEDGE_WORKSHEET_INVALID", $"工作簿只能包含名为{settings.RequiredWorksheetName}的工作表。 ");
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheets[0].Id!);
        if (worksheetPart.Worksheet.Descendants<CellFormula>().Any())
            throw new BusinessException("KNOWLEDGE_FORMULA_FORBIDDEN", "Excel不能包含公式。 ");
        var rows = worksheetPart.Worksheet.GetFirstChild<SheetData>()?.Elements<Row>().ToList() ?? [];
        if (rows.Count == 0) throw new BusinessException("KNOWLEDGE_HEADER_INVALID", "Excel缺少表头。 ");
        var header = GetValues(rows[0], workbookPart);
        if (!Headers.SequenceEqual(header.Take(4)) || header.Skip(4).Any(v => !string.IsNullOrWhiteSpace(v)))
            throw new BusinessException("KNOWLEDGE_HEADER_INVALID", "表头必须依次为：知识编码、标题、文档类型、内容。 ");
        var result = new List<ParsedRow>();
        foreach (var row in rows.Skip(1))
        {
            var values = GetValues(row, workbookPart);
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            result.Add(new((int)(row.RowIndex?.Value ?? (uint)(result.Count + 2)), values.ElementAtOrDefault(0) ?? "", values.ElementAtOrDefault(1) ?? "", values.ElementAtOrDefault(2) ?? "", values.ElementAtOrDefault(3) ?? ""));
        }
        return result;
    }

    private static List<string> GetValues(Row row, WorkbookPart workbookPart)
    {
        var result = new string[4];
        foreach (var cell in row.Elements<Cell>())
        {
            var column = GetColumnIndex(cell.CellReference?.Value);
            if (column is < 0 or > 3) continue;
            result[column] = GetCellText(cell, workbookPart);
        }
        return result.ToList();
    }

    private static int GetColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return -1;
        var letters = reference.TakeWhile(char.IsLetter).ToArray(); var value = 0;
        foreach (var c in letters) value = value * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        return value - 1;
    }

    private static string GetCellText(Cell cell, WorkbookPart workbookPart)
    {
        if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(cell.CellValue?.Text, out var index))
            return workbookPart.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().ElementAtOrDefault(index)?.InnerText ?? "";
        if (cell.DataType?.Value == CellValues.InlineString) return cell.InlineString?.InnerText ?? "";
        if (cell.DataType?.Value == CellValues.Boolean) return cell.CellValue?.Text == "1" ? "TRUE" : "FALSE";
        return cell.CellValue?.Text ?? cell.InnerText ?? "";
    }

    private static void ValidatePackageSafety(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
        if (archive.Entries.Count > 2000 || archive.Entries.Sum(entry => entry.Length) > 52_428_800L)
            throw new BusinessException("KNOWLEDGE_PACKAGE_TOO_LARGE", "Excel解压后内容过大或文件项过多。 ");
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.Contains("vbaProject", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("externalLinks/", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("embeddings/", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("oleObject", StringComparison.OrdinalIgnoreCase))
                throw new BusinessException("KNOWLEDGE_ACTIVE_CONTENT_FORBIDDEN", "Excel包含宏、外链或嵌入对象。 ");
            if (!name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)) continue;
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true, leaveOpen: false);
            var xml = reader.ReadToEnd();
            if (xml.Contains("TargetMode=\"External\"", StringComparison.OrdinalIgnoreCase) || xml.Contains("TargetMode='External'", StringComparison.OrdinalIgnoreCase))
                throw new BusinessException("KNOWLEDGE_EXTERNAL_LINK_FORBIDDEN", "Excel包含外部链接。 ");
        }
    }

    private async Task<long> SaveParsedRowsAsync(string fileName, string hash, List<ParsedRow> rows, long userId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await using var batch = new SqlCommand("INSERT dbo.KnowledgeImportBatch(OriginalFileName,FileHash,Status,CreatedBy) VALUES(@Name,@Hash,'PARSED',@UserId); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", connection, transaction);
            batch.Parameters.Add("@Name", SqlDbType.NVarChar, 260).Value = fileName; batch.Parameters.AddWithValue("@Hash", hash); batch.Parameters.AddWithValue("@UserId", userId);
            var batchId = Convert.ToInt64(await batch.ExecuteScalarAsync(ct));
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var normalized = Normalize(row.Code, row.Title, row.Type, row.Content);
                Classification classification;
                if (!codes.Add(normalized.Code) && normalized.Error is null)
                    classification = new("INVALID", null, "同一Excel中知识编码重复。 ");
                else
                    classification = await ClassifyAsync(connection, transaction, normalized, ct);
                await using var insert = new SqlCommand("""
                    INSERT dbo.KnowledgeImportRow(BatchId,RowNumber,Code,Title,DocumentType,Content,ContentHash,Status,ExistingDocumentId,ErrorMessage)
                    VALUES(@BatchId,@RowNumber,@Code,@Title,@Type,@Content,@Hash,@Status,@ExistingId,@Error)
                    """, connection, transaction);
                insert.Parameters.AddWithValue("@BatchId", batchId); insert.Parameters.AddWithValue("@RowNumber", row.RowNumber);
                AddRowParameters(insert, normalized, classification); await insert.ExecuteNonQueryAsync(ct);
            }
            await RecountBatchAsync(connection, transaction, batchId, ct);
            await transaction.CommitAsync(ct); return batchId;
        }
        catch { await transaction.RollbackAsync(ct); throw; }
    }

    private static NormalizedRow Normalize(string code, string title, string type, string content)
    {
        code = code.Trim().ToUpperInvariant(); title = title.Trim(); type = type.Trim().ToUpperInvariant(); content = content.Trim();
        string? error = null;
        if (!CodePattern.IsMatch(code)) error = "知识编码只允许1至100位字母、数字、短横线和下划线。 ";
        else if (title.Length is < 1 or > 300) error = "标题必须为1至300字。 ";
        else if (!Types.Contains(type)) error = "文档类型无效。 ";
        else if (content.Length is < 1 or > 50000) error = "内容必须为1至50000字。 ";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeContent(content))));
        return new(code, title, type, content, hash, error);
    }

    private static string NormalizeContent(string content) => content.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    private static async Task<Classification> ClassifyAsync(SqlConnection connection, SqlTransaction? transaction, NormalizedRow row, CancellationToken ct)
    {
        if (row.Error is not null) return new("INVALID", null, row.Error);
        await using var command = new SqlCommand("SELECT TOP (1) Id,Title,DocumentType,ContentHash FROM dbo.KnowledgeDocument WHERE Code=@Code", connection, transaction);
        command.Parameters.Add("@Code", SqlDbType.NVarChar, 100).Value = row.Code;
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return new("READY", null, null);
        var id = reader.GetInt64(0); var same = string.Equals(reader.GetString(1), row.Title, StringComparison.Ordinal) && string.Equals(reader.GetString(2), row.Type, StringComparison.Ordinal) && string.Equals(reader.GetString(3), row.Hash, StringComparison.OrdinalIgnoreCase);
        return same ? new("DUPLICATE", id, null) : new("CONFLICT", id, "知识编码已存在且内容不同，请修正后确认。 ");
    }

    private static async Task RecountBatchAsync(SqlConnection connection, SqlTransaction? transaction, long batchId, CancellationToken ct)
    {
        const string sql = """
            UPDATE b SET TotalCount=x.TotalCount,ReadyCount=x.ReadyCount,DuplicateCount=x.DuplicateCount,
              ConflictCount=x.ConflictCount,InvalidCount=x.InvalidCount,
              Status=CASE WHEN x.ConflictCount+x.InvalidCount>0 THEN 'CONFLICT' ELSE 'READY' END,
              UpdateTime=SYSUTCDATETIME()
            FROM dbo.KnowledgeImportBatch b
            CROSS APPLY (SELECT COUNT(*) TotalCount,
              COALESCE(SUM(CASE WHEN Status='READY' THEN 1 ELSE 0 END),0) ReadyCount,
              COALESCE(SUM(CASE WHEN Status='DUPLICATE' THEN 1 ELSE 0 END),0) DuplicateCount,
              COALESCE(SUM(CASE WHEN Status='CONFLICT' THEN 1 ELSE 0 END),0) ConflictCount,
              COALESCE(SUM(CASE WHEN Status='INVALID' THEN 1 ELSE 0 END),0) InvalidCount
              FROM dbo.KnowledgeImportRow WHERE BatchId=b.Id) x
            WHERE b.Id=@Id AND b.Status<>'IMPORTED'
            """;
        await using var command = new SqlCommand(sql, connection, transaction); command.Parameters.AddWithValue("@Id", batchId); await command.ExecuteNonQueryAsync(ct);
    }

    private static void AddRowParameters(SqlCommand command, NormalizedRow row, Classification classification)
    {
        command.Parameters.Add("@Code", SqlDbType.NVarChar, 100).Value = row.Code;
        command.Parameters.Add("@Title", SqlDbType.NVarChar, 300).Value = row.Title;
        command.Parameters.AddWithValue("@Type", row.Type); command.Parameters.Add("@Content", SqlDbType.NVarChar, -1).Value = row.Content;
        command.Parameters.AddWithValue("@Hash", row.Hash); command.Parameters.AddWithValue("@Status", classification.Status);
        command.Parameters.AddWithValue("@ExistingId", (object?)classification.ExistingId ?? DBNull.Value);
        command.Parameters.Add("@Error", SqlDbType.NVarChar, 1000).Value = (object?)classification.Error ?? DBNull.Value;
    }

    private static void AddKnowledgeParameters(SqlCommand command, string code, string title, string type, string content, string hash)
    {
        command.Parameters.Add("@Code", SqlDbType.NVarChar, 100).Value = code; command.Parameters.Add("@Title", SqlDbType.NVarChar, 300).Value = title;
        command.Parameters.AddWithValue("@Type", type); command.Parameters.Add("@Content", SqlDbType.NVarChar, -1).Value = content; command.Parameters.AddWithValue("@Hash", hash);
    }

    private static byte[] ParseRowVersion(string value)
    {
        try { return Convert.FromBase64String(value); }
        catch { throw new BusinessException("INVALID_ROW_VERSION", "并发版本格式无效。 "); }
    }

    private static string SanitizeFileName(string name)
    {
        var clean = Path.GetFileName(name); foreach (var c in Path.GetInvalidFileNameChars()) clean = clean.Replace(c, '_'); return clean.Length <= 260 ? clean : clean[..260];
    }

    private sealed record ParsedRow(int RowNumber, string Code, string Title, string Type, string Content);
    private sealed record NormalizedRow(string Code, string Title, string Type, string Content, string Hash, string? Error);
    private sealed record Classification(string Status, long? ExistingId, string? Error);
}
