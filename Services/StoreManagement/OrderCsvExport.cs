using CameywareOrder.Localization;
using CameywareOrder.Models;

namespace CameywareOrder.Services;

/// <summary>
/// Turns a list of orders into a spreadsheet the shop's bookkeeper can open.
/// </summary>
/// <remarks>
/// The gap this fills: the only export the application had was a zip of the database, which is a
/// migration tool and useless to an accountant. The settlement report is a PDF — a document, not
/// data.
///
/// **Every figure comes from <c>Order.MoneyFor(line)</c> and its siblings**, never re-derived here.
/// That is the standing rule for a second consumer of the money model, and this is the third after
/// the receipt and the settlement report: a sheet that computed its own totals would be free to
/// disagree with the receipt the customer is holding, and a spreadsheet is exactly where nobody would
/// re-check.
///
/// Headers are drawn from the keys the ORDER LIST already uses, so a column called one thing on
/// screen is not called another in the file, and adding a language costs this class nothing.
/// </remarks>
public static class OrderCsvExport
{
    /// <summary>Builds the sheet. Returns the writer so a caller can save it or inspect it.</summary>
    public static CsvWriter Build(IReadOnlyList<Order> orders, ILocalizedText text)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(text);

        var csv = new CsvWriter();
        csv.WriteRow(Headers(text));

        foreach (var order in orders)
            csv.WriteRow(Row(order));

        return csv;
    }

    /// <summary>
    /// A file name for the sheet: the shop, the day, and how many rows it holds.
    /// </summary>
    /// <remarks>
    /// The count is in the name on purpose. A shop that exports "everything" and then a filtered
    /// subset ends up with two files in the same folder, and the row count is the one thing that
    /// tells them apart without opening both.
    /// </remarks>
    public static string SuggestFileName(Shop? shop, string languageCode, int rowCount)
    {
        var name = shop?.ResolveName(languageCode) ?? string.Empty;
        var safe = string.Concat(name.Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
        var prefix = string.IsNullOrEmpty(safe) ? "orders" : safe;

        return $"{prefix}-{DateTime.Now:yyyyMMdd}-{rowCount}.csv";
    }

    private static List<object?> Headers(ILocalizedText text)
    {
        var headers = new List<object?>
        {
            text["Order.Fields.OrderNumber"],
            text["Order.Fields.OrderDate"],
            text["Order.Fields.ExpectedPickupDate"],
            text["Order.Fields.Status"],
            text["Order.Fields.BalanceStatus"],
            text["Order.Fields.CustomerName"],
            text["Order.Fields.PhoneNumber"],
            text["Order.Fields.Email"],
            text["Order.Fields.Address"],
            text["Order.Fields.ServiceType"],
            text["Csv.Column.Currency"],
        };

        // Three columns per service line. The header SHAPE is a string-table format
        // (`Csv.Column.Line`) rather than two words joined in C#. A language that brackets the
        // qualifier, orders the two the other way round, or uses fullwidth punctuation between them
        // cannot be produced by pasting a separator between two fragments — the same rule this
        // codebase already keeps for every other composed line.
        foreach (var line in ServiceLines.All)
        {
            var name = text[ServiceLines.NameKey(line)];
            headers.Add(text.Format("Csv.Column.Line", name, text["Settlement.PreTax"]));
            headers.Add(text.Format("Csv.Column.Line", name, text["Settlement.Tax"]));
            headers.Add(text.Format("Csv.Column.Line", name, text["Settlement.PostTax"]));
        }

        headers.AddRange(new object?[]
        {
            text["Order.Fields.Subtotal"],
            text["Order.Fields.TaxAmount"],
            text["Order.Fields.TotalAmount"],
            text["Order.Fields.ReceivedDownpayment"],
            text["Order.Fields.ReceivedFinalBalance"],
            text["Order.Fields.FinalBalance"],
            text["Order.Fields.Notes"],
            text["Order.Fields.LastModifiedDate"],
            text["Order.Fields.LastModifiedBy"],
        });

        return headers;
    }

    private static List<object?> Row(Order order)
    {
        var row = new List<object?>
        {
            order.OrderNumber,
            order.OrderDateLocal,
            // A DateOnly rather than a DateTime: the pickup day carries no meaningful time, and
            // rendering it "2026-08-14 00:00" invites a reader to think it does.
            order.ExpectedPickupDateLocal is { } pickup ? DateOnly.FromDateTime(pickup) : null,
            order.Status.ToString(),
            order.PaymentStatusKind.ToString(),
            order.CustomerName,
            order.PhoneNumber,
            order.Email,
            order.Address,
            order.ServiceType.ToString(),
            order.CurrencyType.ToString(),
        };

        foreach (var line in ServiceLines.All)
        {
            var money = order.MoneyFor(line);
            row.Add(money.Subtotal);
            row.Add(money.Tax);
            row.Add(money.Total);
        }

        row.AddRange(new object?[]
        {
            order.AlterationMoney.Subtotal + order.CustomMadeMoney.Subtotal + order.ClothingMoney.Subtotal,
            order.TotalTax,
            order.TotalAmount,
            order.ReceivedDownpayment,
            order.ReceivedFinalBalance,
            order.FinalBalance,
            order.AdditionalNotes,
            order.LastModifiedDate?.ToLocalTime(),
            order.LastModifiedBy,
        });

        return row;
    }
}
