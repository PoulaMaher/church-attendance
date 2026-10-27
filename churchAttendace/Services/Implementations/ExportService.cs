using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using churchAttendace.Services.Interfaces;

namespace churchAttendace.Services.Implementations
{
    public class ExportService : IExportService
    {
        private static bool _licenseInitialized;
        private static readonly object _licenseLock = new();

        private static void EnsureLicense()
        {
            if (!_licenseInitialized)
            {
                lock (_licenseLock)
                {
                    if (!_licenseInitialized)
                    {
                        try
                        {
                            QuestPDF.Settings.License = LicenseType.Community;
                        }
                        catch
                        {
                            // Avoid throwing during license check
                        }
                        _licenseInitialized = true;
                    }
                }
            }
        }

        public byte[] ExportToExcel(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.AddWorksheet(sheetName);

            for (var i = 0; i < headers.Count; i++)
            {
                worksheet.Cell(1, i + 1).Value = headers[i];
            }

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                for (var colIndex = 0; colIndex < row.Count; colIndex++)
                {
                    worksheet.Cell(rowIndex + 2, colIndex + 1).Value = row[colIndex];
                }
            }

            worksheet.Columns().AdjustToContents();
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public byte[] ExportToPdf(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
        {
            return ExportToPdf(title, headers, rows, null, null);
        }

        public byte[] ExportToPdf(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, IReadOnlyList<float>? columnWeights, string? subtitle = null)
        {
            if (!Environment.Is64BitProcess)
            {
                throw new InvalidOperationException("تصدير PDF يتطلب تشغيل الخادم بمعمارية 64-bit (win-x64). يرجى تغيير إعدادات النشر لتكون x64.");
            }

            EnsureLicense();

            var isLandscape = headers.Count > 5;
            var fontSize = headers.Count > 8 ? 9.2f : 10.5f;

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    if (isLandscape)
                    {
                        page.Size(PageSizes.A4.Landscape());
                    }
                    else
                    {
                        page.Size(PageSizes.A4);
                    }

                    page.ContentFromRightToLeft();
                    page.Margin(15);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(fontSize));

                    page.Header()
                        .PaddingBottom(8)
                        .Row(row =>
                        {
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text(title).FontSize(15).Bold().FontColor("#1e293b");
                                var metaText = string.IsNullOrWhiteSpace(subtitle)
                                    ? $"تاريخ الاستخراج: {DateTime.Now:yyyy/MM/dd HH:mm} | إجمالي السجلات: {rows.Count}"
                                    : subtitle;
                                col.Item().PaddingTop(2).Text(metaText).FontSize(8.8f).FontColor("#64748b");
                            });
                        });

                    page.Content()
                        .Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                if (columnWeights != null && columnWeights.Count == headers.Count)
                                {
                                    for (var i = 0; i < columnWeights.Count; i++)
                                    {
                                        if (headers[i] == "م" || headers[i] == "#")
                                        {
                                            columns.ConstantColumn(28);
                                        }
                                        else
                                        {
                                            columns.RelativeColumn(columnWeights[i]);
                                        }
                                    }
                                }
                                else
                                {
                                    for (var i = 0; i < headers.Count; i++)
                                    {
                                        if (headers[i] == "م" || headers[i] == "#")
                                        {
                                            columns.ConstantColumn(28);
                                        }
                                        else
                                        {
                                            columns.RelativeColumn();
                                        }
                                    }
                                }
                            });

                            table.Header(header =>
                            {
                                for (var i = 0; i < headers.Count; i++)
                                {
                                    header.Cell()
                                        .Background("#1e3a8a")
                                        .PaddingVertical(5)
                                        .PaddingHorizontal(3)
                                        .Border(0.5f)
                                        .BorderColor("#94a3b8")
                                        .AlignCenter()
                                        .AlignMiddle()
                                        .Text(headers[i])
                                        .Bold()
                                        .FontColor(Colors.White)
                                        .FontSize(fontSize);
                                }
                            });

                            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                            {
                                var row = rows[rowIndex];
                                var bg = rowIndex % 2 == 1 ? Colors.Grey.Lighten4 : Colors.White;

                                for (var colIndex = 0; colIndex < row.Count; colIndex++)
                                {
                                    table.Cell()
                                        .Background(bg)
                                        .PaddingVertical(4)
                                        .PaddingHorizontal(3)
                                        .Border(0.5f)
                                        .BorderColor("#e2e8f0")
                                        .AlignCenter()
                                        .AlignMiddle()
                                        .Text(row[colIndex])
                                        .FontSize(fontSize - 0.5f)
                                        .FontColor("#334155");
                                }
                            }
                        });

                    page.Footer()
                        .PaddingTop(8)
                        .Row(row =>
                        {
                            row.RelativeItem().AlignLeft().Text("نظام إدارة خدمة الحضور والافتقاد - كنيسة مارمينا").FontSize(8).FontColor("#94a3b8");
                            row.RelativeItem().AlignRight().Text(x =>
                            {
                                x.DefaultTextStyle(s => s.FontSize(8).FontColor("#64748b"));
                                x.Span("صفحة ");
                                x.CurrentPageNumber();
                                x.Span(" من ");
                                x.TotalPages();
                            });
                        });
                });
            });

            return document.GeneratePdf();
        }
    }
}
