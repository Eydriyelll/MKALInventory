namespace MkalInventoryApp.Services;

/// <summary>One object that hands each screen the four module services.</summary>
public class ServiceHub
{
    public AppDatabase Database { get; }
    public AuthService Auth { get; }
    public InventoryService Inventory { get; }
    public CirculationService Circulation { get; }
    public ComplianceService Compliance { get; }

    public ServiceHub(AppDatabase database, AuthService auth, InventoryService inventory,
        CirculationService circulation, ComplianceService compliance)
    {
        Database = database;
        Auth = auth;
        Inventory = inventory;
        Circulation = circulation;
        Compliance = compliance;
    }
}
