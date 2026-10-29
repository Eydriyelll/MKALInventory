# MKAL Inventory v2

Workplace Tool, Equipment Calibration and Custody Inventory System for MKAL Electric Enterprise
(IT111L Concepts in OOP, machine problem). .NET MAUI app for **Windows, macOS (Mac Catalyst) and Android**.

## 1. Install the files
1. Copy this folder over your project. Replace `App.xaml`, `App.xaml.cs`, `MauiProgram.cs`, `MkalInventoryApp.csproj`,
   and the whole `Models/`, `Services/` and `Views/` folders.
2. **Delete the old versions first** so there are no duplicate classes: the old `Models/*.cs`, `Services/DatabaseService.cs`,
   `Views/*`, and the `SignatureDrawable` class inside the old `UserDashboardPage.xaml.cs`.
3. Leave `Platforms/`, `Resources/` and `Properties/` as they are, then make the small edits in `Platforms-setup/`
   (camera permission for Android; camera, photo and file-save permissions for Mac).
4. The database file is new (`mkal_inventory_v3.db`), so old data is not carried over.

Optional: delete `AppShell.xaml(.cs)` and `MainPage.xaml(.cs)`. They are template leftovers and are not used.

## 2. Run it
| Where | Command (run in the project folder) |
|---|---|
| Windows | `dotnet run -f net10.0-windows10.0.19041.0` |
| Mac | `dotnet workload install maui` (once), then `dotnet build -t:Run -f net10.0-maccatalyst` |
| Android | `dotnet build -t:Run -f net10.0-android -p:IncludeAndroid=true` with an emulator or phone connected (needs the Android workload: `dotnet workload install maui-android`, plus Android SDK and JDK) |

Mac needs Xcode installed and opened once. If a build complains about a missing workload, run `dotnet workload install maui`.

## 3. Demo accounts
| Username | Password | Handover PIN | Sign-off initials | Role |
|---|---|---|---|---|
| admin | Admin@123 | 9999 | MA | Administrator / QTP |
| technician | Tech@123 | 1234 | FE | Field technician (Journeyman) |
| foreman | Tech@123 | 4321 | SF | Field technician (Foreman) |

## 4. Proposal checklist
| Proposal item | Where it lives |
|---|---|
| (a) Role-based access control | `AuthService`, admin checks re-read from the database in every admin method |
| (b) Dual-custody handover (PIN + initials) | `CirculationService.BorrowAsync`, `AuthService.VerifyHandover` (PIN **and** registered initials must match) |
| (c) Calibration and safety lockout | `InventoryService.EnforceCalibration`, run by every scan, checkout and return |
| (d) Return inspection and photo log | `CirculationService.ReturnAsync` (Available, Needs Maintenance, Damaged, Lost, notes, photo path) |
| (e) Job-site date reservations | `CirculationService.ReserveAsync` (one booking per tool per day, checkout never overruns a booking) |
| (f) Audit trail and closeout CSV | `ComplianceService` (hash-chained ledger, Verify ledger) and `CsvReport` subclasses |
| Policy 1 Calibration lockout | scan, checkout and return all lock expired tools; Release refuses until the date is renewed |
| Policy 2 Quarantine | Damaged and Needs Maintenance block reissue until an admin releases the tool |
| Policy 3 Custody sign-off | no tool becomes Borrowed without PIN and initials |
| Policy 4 Shift accountability | return window (end of shift, tomorrow, 3 days, or any calendar date up to 90 days away); overdue sets status and adds +10 risk once per loan |
| Policy 5 Single borrower | only the registered borrower can return it |
| Output 1 Admin console | Overview, Tools table (ItemId, Name, Description, Status, Next Calibration Due, Custody), Add Tool, Lock for Calibration, Scan Overdue Tools, Export Audit Report (CSV), Refresh |
| Output 2 Technician workspace | equipment grid, Handover fields, Return and Inspection, Reservations, Check Out/Borrow, Check In/Return, Refresh Catalog |

### Group modules (one service class each)
| Member | Module | Class |
|---|---|---|
| Adriel Lewis Araos | Authentication, role security, handover verification, database lifecycle | `AuthService`, `AppDatabase` |
| Jeremy Caagbay | Calibration, safety lockout, inventory | `InventoryService` |
| Shan Myro Nuñez | Circulation, staging, return assessment, reservations | `CirculationService` |
| Katrina Nicole Robrigado | Audit ledger, risk scoring, compliance export | `ComplianceService`, `Reports.cs` |

### OOP concepts used (for the lab write-up)
- **Encapsulation:** `AppDatabase` hides the connection and audit hashing; services expose only intent-level methods.
- **Inheritance:** `AuthService`, `InventoryService`, `CirculationService` and `ComplianceService` all extend `ServiceBase`; the three reports extend `CsvReport`.
- **Polymorphism:** `CustodyReport`, `AuditLedgerReport` and `InventoryReport` override `Headers` and `LoadRowsAsync`; the UI exports any of them through the same `ExportAsync(CsvReport)` call. `SignatureDrawable` implements `IDrawable`.
- **Abstraction:** `CsvReport` is abstract; screens talk to `ServiceHub`, not to SQL.

## 5. Tests
`../LogicTests` compiles the models and services against a small in-memory stand-in for SQLite and runs 91 scenario checks
(every policy above, admin-only enforcement, reservations, ledger tamper detection, CSV safety). Run: `dotnet run --project LogicTests`.
These tests do **not** cover the screens or real SQLite; try the click-through below for those.

## 6. Click-through test (5 minutes)
1. Sign in as `admin`. Overview shows MTR-004 (calibration expired) locked automatically. Open the Tools tab: search, filter, sort, tap a row to edit.
2. Add a tool, lock it, release it, delete it. Open Audit ledger, tap **Verify ledger integrity**.
3. Sign out, sign in as `technician`. Tap ISO-001, enter PIN `1234` and initials `FE`. Try a wrong PIN, then wrong initials, then MTR-004 (denied).
4. Sign in as `foreman`: try to return ISO-001 (denied). Reserve DRL-007 for tomorrow.
5. As `technician`, return ISO-001 as Damaged; as `admin`, see it quarantined and release it.
6. Admin: **Export audit report (CSV)** and open the file.

## 7. If something goes wrong
- Errors are written to `mkal_crash.log` next to the app (`bin/.../mkal_crash.log`) and on the Desktop.
- On Windows a silent crash also appears in Event Viewer, Windows Logs, Application.
- NU1903 warnings about `SQLitePCLRaw` are a package advisory and do not stop the build.
