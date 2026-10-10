# 企業訓練管理 Web API

ASP.NET Core 10 Web API、EF Core 10、SQL Server，以及 JWT Bearer 登入。課程、員工、報名與儀表板 API 全部需要有效 JWT；註冊、登入與健康檢查允許匿名。資料庫結構由版本化 EF Core migrations 建立與更新，不使用 SQLite 或 `EnsureCreated`。

從專案根目錄執行開發啟動腳本，會準備未納入版本控制的 `.runtime/development.secrets.json`。單獨啟動後端時，Development 環境亦會讀取該檔案；環境變數與命令列設定會覆寫其中內容。

```powershell
dotnet restore
dotnet run
```

預設 API 位址 `http://localhost:5080`。Angular 的 `http://localhost:4200` 與 `http://127.0.0.1:4200` 可呼叫 API。Development 環境提供 `/openapi/v1.json`，此端點也需要 JWT。

## SQL Server 與設定

預設本機連線使用 SQL Server `localhost`、`TrainingManagement` 資料庫及 Windows 驗證。啟動帳號需具備建立資料庫與套用 migrations 的權限；既有正式資料庫可改由部署程序先套用 migrations。開發連線的 `TrustServerCertificate=True` 僅供本機自簽憑證；正式環境應使用受信任憑證並設定 `TrustServerCertificate=False`。

| 設定／環境變數 | 用途 |
| --- | --- |
| `ConnectionStrings__TrainingDatabase` | SQL Server 連線字串 |
| `Jwt__SigningKey` | 必填的 HS256 簽章金鑰，UTF-8 至少 32 bytes；程式不提供預設值 |
| `Jwt__Issuer` | 預設 `TrainingManagement.Api` |
| `Jwt__Audience` | 預設 `TrainingManagement.Web` |
| `Jwt__ExpiresMinutes` | 預設 30，範圍 1 至 120 分鐘 |
| `BootstrapAdmin__Username` | 全新 AppUsers 資料表的初始管理員帳號 |
| `BootstrapAdmin__Password` | 初始管理員密碼，12 至 256 字元 |
| `BootstrapAdmin__DisplayName` | 初始管理員顯示名稱 |
| `LegacyImportPath` | 可選的一次性舊版 JSON 快照匯入路徑 |
| `SeedDemoData` | Development 環境是否建立展示資料，預設 true |
| `RateLimits__RegisterPermitLimit` | 每個來源 IP 每分鐘的註冊請求上限，預設 5，允許 1 至 100；測試環境可調高以驗證多個案例 |

初始管理員僅在 AppUsers 表完全空白時建立，使用 ASP.NET Core `PasswordHasher<AppUser>` 雜湊密碼。修改 BootstrapAdmin 設定不會覆寫既有帳號或密碼；正式環境請將密碼與金鑰放在安全的設定來源，不要提交至版本控制。

Development 示範資料只在全新、未匯入的核心資料表建立，日期相對於當日。既有資料庫啟動與重新啟動不會覆寫資料。

## 登入與 API

`POST /api/auth/register` 接受 `{ username, displayName, password, confirmPassword }`，成功回傳 201 與 `{ id, username, displayName, role, employeeId }`（新帳號的 `employeeId` 為 null），不會回傳密碼、雜湊或 JWT。完成後使用正常登入流程。新帳號固定為 Employee、啟用狀態；不能傳入角色、識別碼或帳號狀態等其他欄位。

帳號會去除前後空白，限 3 至 80 字元的 ASCII 英文、數字、點、底線及連字號，不區分大小寫且不得重複。顯示名稱去除前後空白後必填且最多 100 字元。密碼不得為空白，長度 12 至 256 字元；兩次輸入必須完全相同。無效輸入回傳 400，重複帳號（包含同時註冊競爭）回傳 409。註冊使用獨立的每 IP 每分鐘 5 次限制，超過回傳 429；不會占用登入額度。

Employee 可查看課程、員工、報名及統計，並使用個人身分查詢與登出。管理員從 `/api/accounts` 找到待處理註冊帳號，再以 `POST /api/accounts/{id}/employee` 傳入完整員工資料，在單一交易中新增員工並對應帳號。帳號與員工一對一，員工編號不能由註冊者自行指定；已具員工資料的帳號回傳 409。完成後 Employee 可對 `/api/enrollments/self` 傳入 `{ "courseId": 123 }` 自行報名，由 `/api/enrollments/mine` 查詢自己的紀錄，或以 `POST /api/enrollments/{id}/cancel` 取消自己的已報名／學習中紀錄。取消後紀錄保留但不占名額；已完訓及重複取消回傳 409，取消他人紀錄回傳 403。只有 Admin 可以新增、修改或刪除課程、員工與他人的報名紀錄。未登入存取受保護資源回傳 401；一般帳號嘗試管理寫入回傳 403。Bootstrap 管理員維持 Admin 權限。

`POST /api/auth/login` 使用 `{ "username": "帳號", "password": "密碼" }` 登入，成功回傳 accessToken、tokenType、expiresAt，以及 `{ id, username, displayName, role, employeeId }` 使用者物件。帳號不區分大小寫；密碼區分大小寫。

之後在請求加入 `Authorization: Bearer <accessToken>`。`GET /api/auth/me` 取得目前使用者。`POST /api/auth/logout` 回傳 204，將資料庫 TokenVersion 加一，因此該使用者目前所有既有 token 都會立即失效。過期或失效後重新登入；目前未提供 refresh token。

JWT 驗證包含 issuer、audience、簽章、HS256 演算法、到期時間，以及資料庫帳號是否啟用和 TokenVersion。登入錯誤統一回傳 401；每個來源 IP 每分鐘最多 10 次登入請求，超過回傳 429。

| 路徑 | 方法 | 功能 |
| --- | --- | --- |
| `/api/health` | GET | 匿名服務狀態 |
| `/api/auth/register` | POST | 匿名註冊一般使用者 |
| `/api/auth/login` | POST | 匿名登入 |
| `/api/auth/me` | GET | 目前使用者 |
| `/api/auth/logout` | POST | 登出與撤銷既有 token |
| `/api/dashboard` | GET | 完訓率、部門統計、近六個月趨勢 |
| `/api/courses` | GET, POST | 課程列表與新增 |
| `/api/courses/{id}` | GET, PUT, DELETE | 單筆課程查詢與維護 |
| `/api/employees` | GET, POST | 員工列表與新增 |
| `/api/employees/{id}` | GET, PUT, DELETE | 單筆員工查詢與維護 |
| `/api/accounts` | GET | 管理員查詢一般使用者帳號與員工建立狀態 |
| `/api/accounts/{id}/employee` | POST | 管理員為帳號建立全新員工資料 |
| `/api/enrollments` | GET, POST | 報名紀錄與新增報名 |
| `/api/enrollments/mine` | GET | 一般使用者查詢自己的報名 |
| `/api/enrollments/self` | POST | 一般使用者以已建立的員工身分報名 |
| `/api/enrollments/{id}/cancel` | POST | 一般使用者取消自己的有效報名 |
| `/api/enrollments/{id}` | GET, PUT, DELETE | 訓練進度、完訓與紀錄維護 |

完整範例請見 `TrainingManagement.Api.http`。所有日期輸入接受 ISO 8601，輸出使用 UTC（`Z`）；月份統計採臺北時區（UTC+8）。JSON 欄位為 camelCase、列舉為字串。PUT 採完整資料覆寫；狀態、進度等必要欄位缺漏回傳 400。

課程僅在 Published 且員工啟用時可報名；名額排除 Cancelled 紀錄。重複有效報名、額滿及唯一員工編號／郵件回傳 409。取消報名後再次報名會重設進度、成績、完訓日期及報名日期。完訓要求進度為 100%；重新開啟紀錄會清除完訓日期。已有任何報名紀錄的課程或員工不可直接刪除。

寫入由應用程式同步閘控制；涉及名額的操作另在 SQL Server 交易內取得每門課程的 `sp_getapplock`，避免跨 API 執行個體同時報名超額。資料表設有 `(CourseId, EmployeeId)` 唯一索引。業務錯誤與輸入錯誤回傳 RFC Problem Details。

儀表板的 employeeCount 計算啟用員工、courseCount 計算全部課程、activeCourseCount 計算 Published 課程。報名總數與完訓率排除 Cancelled 紀錄；訓練時數是各筆 Completed 紀錄所對應的課程時數總和（學員人時）。停用員工的既有完訓紀錄仍保留於統計。月份以報名日期或完訓日期歸屬。

## 舊版資料匯入與 migrations

`LegacyImportPath` 接受舊 API 匯出的 `{ courses: [], employees: [], enrollments: [] }` JSON。僅在三張核心表全空白時匯入，先驗證來源識別碼、唯一值、關聯、狀態、時間與報名統計，再於單一 SQL Server 交易建立資料。SQL Server identity 會產生新識別碼，所有報名外鍵均對應至正確的新識別碼；舊 JSON 與 SQLite 檔案保持原樣。快照缺漏或資料不一致會中止啟動，避免改以示範資料掩蓋失敗。目標表已有資料則跳過匯入。

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations list
dotnet tool run dotnet-ef migrations add YourChange --output-dir Data/Migrations
```

後續資料結構變更需新增 migration。正式部署還需配置 HTTPS、管理員生命週期、稽核與備份；若整合企業單一登入，應改採標準 OIDC 身分提供者。
