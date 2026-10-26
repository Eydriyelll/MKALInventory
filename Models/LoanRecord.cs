using SQLite;

namespace MkalInventoryApp.Models;

public class LoanRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string BorrowerName { get; set; } = string.Empty;
    public DateTime BorrowDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public string ReturnCondition { get; set; } = string.Empty;
    public string ReturnNotes { get; set; } = string.Empty;
    public string SignatureInitials { get; set; } = string.Empty;
    public string SignatureData { get; set; } = string.Empty;
    public string PhotoPath { get; set; } = string.Empty;
    public bool OverdueFlagged { get; set; }
}
