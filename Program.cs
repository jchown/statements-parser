using System.Text;
using System.Text.RegularExpressions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

var folder = $"{Environment.GetEnvironmentVariable("DROPBOX")}\\Beetham Plaza\\Consolidated Accounts";
        
var statements = Directory.GetFiles(folder, "*.pdf");

List<Transaction> transactions = new();
        
foreach (var statement in statements)
{
    Console.WriteLine("Reading " + statement);

    var pages = ReadPages(statement, out var numPages);

    if (pages.Contains("Barclays Bank UK PLC"))
    {
        ParseBarclays(statement, pages, numPages);
    }
    else
    {
        throw new Exception();
    }
}

void ParseBarclays(string filename, string pages, int numPages)
{
    for (int i = 1; i <= numPages; i++)
        pages = pages.Replace($"Page {i} of {numPages}\n", "");
            
    Console.WriteLine("Parsing Barclays " + filename);

    bool StartBarclaysTransactions(string line)
    {
        //  Date Description Money in Money out Balance

        return line.StartsWith("Date") && line.EndsWith("Balance");
    }

    string TakeNext(LinkedList<string> linkedList)
    {
        var next = linkedList.First().Trim();
        linkedList.RemoveFirst();
        return next;
    }

    (int transferred, int balance) ParseBarclaysTransfer(string line)
    {
        var elements = line.Split(" ");
        return (ParsePounds(elements[0]), ParsePounds(elements[^1]));
    }

    var lines = new LinkedList<string>(pages.Split("\n").SkipWhile(line => !StartBarclaysTransactions(line.Trim())).Skip(1));

    while (lines.Count > 4)
    {
        
        var date = TakeNext(lines);
        var type = TakeNext(lines);

        if (type.Length > 10 && IsDate(type.Substring(0, 10))) // At page starts, the payee may be merged with the date.
        {
            lines.AddFirst(type.Substring(10).Trim());
            type = type.Substring(0, 10).Trim();
        }

        if (IsDate(type, false) && IsType(date))      // At page ends, the type may be on the last line and hence appear before the date.
            Swap(ref type, ref date);

        if (!IsDate(date))
            break;

        var payee = TakeNext(lines);
        var reference = TakeNext(lines);
        
        if (type == "Direct Debit" && lines.First().StartsWith("FIRST DDR"))
            TakeNext(lines);

        var transfer = TakeNext(lines);
        
        if (!IsTransfer(transfer) && IsTransfer(reference)) // At page ends, the transfer may appear before the reference.
            Swap(ref reference, ref transfer);
        
        var (transferred, balance) = ParseBarclaysTransfer(transfer);
                
        var transaction = new Transaction
        {
            Date = date,
            Type = type,
            Payee = payee,
            Reference = reference,
            TransferredPence = transferred,
            BalancePence = balance
        };
        
        Console.WriteLine(transaction);

        transactions.Add(transaction);
    }
}

string ReadPages(string filename, out int numPages)
{
    using var pdfReader = new PdfReader(filename);
    using var pdf = new PdfDocument(pdfReader);
            
    numPages = pdf.GetNumberOfPages();

    string pages = "";
            
    for (int i = 1; i <= numPages; i++)
    {
        Console.WriteLine("Reading page " + i + "/" + numPages);
        var page = pdf.GetPage(i);
                
        var strategy = new SimpleTextExtractionStrategy();
        var currentText = PdfTextExtractor.GetTextFromPage(page, strategy);
                
        pages += Encoding.UTF8.GetString(Encoding.Convert(Encoding.Default, Encoding.UTF8, Encoding.Default.GetBytes(currentText))) + "\n";
    }

    return pages;
}

bool IsTransfer(string s)
{
    Regex transferRegex = new(@"^(-)?£(\d{1,3}(,\d{3})*)(\.\d{2})?\s+(-)?£(\d{1,3}(,\d{3})*)(\.\d{2})?$");
    
    return transferRegex.IsMatch(s);
}

//  Regex to parse e.g. "£1,617.63" or "-£926.90"

int ParsePounds(string pounds)
{
    Regex parser = new(@"^(-)?£(\d{1,3}(,\d{3})*)(\.\d{2})?$");

    var match = parser.Match(pounds);
    if (!match.Success)
        throw new Exception("Failed to parse " + pounds);
    
    var negative = match.Groups[1].Success;
    var digits = match.Groups[2].Value.Replace(",", "");
    var pence = int.Parse(digits) * 100;
    if (match.Groups[4].Success)
        pence += int.Parse(match.Groups[4].Value.Substring(1, 2));
    return negative ? -pence : pence;
}

bool IsDate(string s, bool strict = true)
{
    Regex parser = strict ? new(@"^\d{2}/\d{2}/\d{4}$") : new(@"^\d{2}/\d{2}/\d{4}");

    return parser.IsMatch(s);
}

bool IsType(string s)
{
    switch (s)
    {
        case "Credit":
        case "Direct Debit":
        case "Standing Order":
        case "Funds Transfer":
        case "Bill Payment":
            return true;
        
        default:
            return false;
    
    }
}

void Swap(ref string s1, ref string s2)
{
    (s1, s2) = (s2, s1);
}

record Transaction
{
    public string Date { get; set; }
    public string Type { get; set; }
    public string Payee { get; set; }
    public string Reference { get; set; }
    public int TransferredPence { get; set; }
    public int BalancePence { get; set; }
};
