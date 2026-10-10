# 育成｜企業訓練管理系統

繁體中文企業訓練管理系統，前端使用 Angular，後端使用 ASP.NET Core Web API，資料保存於 SQL Server，使用 JWT 驗證登入。

## 功能

- **總覽**：員工數、課程數、完訓率、訓練時數、部門統計與近期課程。
- **課程管理**：搜尋、篩選、新增及編輯課程，設定日期、講師、時數、名額、授課方式與狀態。
- **員工與帳號管理**：維護員工資料；管理員可查看新註冊帳號，核對後為該帳號建立新員工。
- **報名與進度**：課程報名、一般使用者取消自己的報名，以及進度、完訓與成績管理。
- **訓練報表**：實際訓練紀錄、部門統計及 Excel 相容的中文 CSV 匯出。
- **登入與登出**：JWT 保護管理 API；重新整理時向伺服器確認身分，登出會撤銷該帳號已發行的 Token。
- **帳號註冊與自行報名**：新帳號為一般使用者；管理員建立其員工資料後，使用者可自行報名已發布課程，也可取消自己的有效報名。課程、員工與學習進度仍由管理員維護。

## 開發環境

- .NET SDK 10.0（目標 `net10.0`）
- Node.js `^22.22.3`、`^24.15.0` 或 `>=26.0.0`，以及 npm
- SQL Server；預設使用本機 `localhost` 執行個體與 Windows 驗證

本機已使用 .NET SDK 10.0.400、Node.js 24.18.1、SQL Server 17.0.1000.7。Angular 相容版本見 [官方版本表](https://angular.dev/reference/versions)；SQL Server Provider 見 [EF Core 文件](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/)。執行 API 的 Windows 帳號須能建立及存取開發資料庫。

## 快速啟動（Windows PowerShell）

在專案根目錄執行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Start-Dev.ps1
```

第一次執行會產生本機登入設定、安裝缺少的前端套件並建置後端。啟動後開啟 **http://localhost:4200**；API 位址為 **http://localhost:5080**。服務在背景執行，日誌保存於 `.runtime/`。

初始帳號為 `admin`。首次建立帳號使用的隨機密碼位於 **`.runtime/development.secrets.json` 的 `BootstrapAdmin.Password`**。此檔案也保存開發 JWT 金鑰，已排除版本控制；請保留檔案，勿分享或提交。資料庫只保存密碼雜湊。既有帳號不會因修改 bootstrap 設定而重設密碼。

停止腳本啟動的服務：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Stop-Dev.ps1
```

也可分別在兩個終端機啟動。先產生本機設定，再啟動 API：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Initialize-DevSecrets.ps1
dotnet run --project .\backend\TrainingManagement.Api.csproj
```

```powershell
cd frontend
npm ci
npm start
```

Angular 開發伺服器將 `/api` 請求代理到後端。

## SQL Server 與資料遷移

開發預設連線：

```text
Server=localhost;Database=TrainingManagement;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=10
```

可在啟動前透過 `ConnectionStrings__TrainingDatabase` 環境變數指定其他執行個體、資料庫或驗證方式。帳密應由環境變數或部署平台的秘密設定提供，勿寫入版控檔案。`TrustServerCertificate=True` 用於本機開發；正式 SQL Server 應使用有效憑證並設為 `False`。

API 啟動時使用已提交的 EF Core migrations 建立或升級結構。資料表包含 `Courses`、`Employees`、`Enrollments`、`AppUsers` 及 `__EFMigrationsHistory`。SQL Server 資料由資料庫服務保存，備份使用 SQL Server 的資料庫備份功能。

本次切換前的 12 門課程、20 位員工、88 筆報名已匯出至 `.runtime/legacy-export.json`。開發啟動腳本會在核心資料表全空時，以交易匯入資料，保留進度、成績及歷史時間；既有 SQL Server 資料不會被覆蓋。原 SQLite 檔案 `backend/Data/training.db` 保留供還原參考，目前 API 使用 SQL Server。

第一次手動啟動若要匯入舊快照，請先設定 `LegacyImportPath` 為該 JSON 的絕對路徑。開發環境的空資料庫若未匯入快照，會建立示範資料；可透過 `SeedDemoData=false` 停用。

## JWT 設定

開發腳本產生隨機簽章金鑰及初始管理員密碼。正式環境由秘密管理設定提供：

| 設定 | 用途 |
| --- | --- |
| `ConnectionStrings__TrainingDatabase` | SQL Server 連線字串 |
| `Jwt__SigningKey` | 至少 32 個 UTF-8 bytes 的秘密金鑰 |
| `Jwt__Issuer` | 發行者，預設 `TrainingManagement.Api` |
| `Jwt__Audience` | 接收者，預設 `TrainingManagement.Web` |
| `Jwt__ExpiresMinutes` | Token 有效分鐘數，預設 30 |
| `BootstrapAdmin__Username` | 首次建立管理員的帳號 |
| `BootstrapAdmin__Password` | 首次密碼，至少 12 字元 |
| `BootstrapAdmin__DisplayName` | 管理員顯示名稱 |
| `RateLimits__RegisterPermitLimit` | 每個 IP 每分鐘的註冊請求上限，預設 5，範圍 1 至 100 |

API 驗證簽章、演算法、發行者、接收者、期限，以及帳號啟用狀態和 Token 版本。登入與註冊嘗試按 IP 分別限制頻率。前端將 Token 保存在 `sessionStorage`，到期或 API 回傳 401 時重新登入；重新載入時透過 `/api/auth/me` 驗證身分。登出會使該帳號目前所有登入工作階段的 Token 失效。管理員（`Admin`）可維護資料與帳號綁定；一般使用者（`Employee`）可檢視、匯出，並在綁定後自行報名與取消自己的有效報名。尚無部門分級權限。

正式部署應透過 HTTPS 提供前端與 API，並依實際網域設定 CORS、主機名稱及秘密管理。JWT 設計參考 [ASP.NET Core 官方文件](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0)。

## 註冊

在登入頁選擇註冊，或直接開啟 **http://localhost:4200/register**。填入帳號、顯示名稱、密碼及確認密碼。

- 帳號為 3 至 80 字元，可使用英文字母、數字、句點、底線及連字號；不區分大小寫。
- 顯示名稱必填，最多 100 字元。
- 密碼為 12 至 256 字元，確認密碼須完全一致。
- 重複帳號會提示衝突；失敗時保留表單內容。
- 成功建立後返回登入頁並帶入帳號；密碼不會放在網址或瀏覽器儲存空間。

新帳號固定為一般使用者，API 不接受自行指定管理員權限。註冊後，管理員可在「員工管理 → 註冊帳號」看到帳號；待建立員工者會排在前面。若管理員已開著頁面，按「重新整理帳號」即可取得剛註冊的帳號。一般使用者在同一頁的「我的登入帳號」可查看自己的員工資料狀態。註冊不會自動新增人事員工，也不接受自行指定員工身分。

管理員核對身分後，從待處理帳號按「建立新員工」，填寫員工編號、姓名、電子郵件、部門與職稱。系統會在同一筆交易中新增員工並對應該帳號，不會選用名冊裡的既有員工。每個帳號只會對應一筆員工資料。一般使用者完成後，進入「課程管理」即可按「立即報名」，已報名課程會顯示狀態及「取消報名」按鈕；「報名與學習紀錄」也可取消自己的有效報名。取消前會要求確認，取消後保留紀錄並釋出名額；已完訓不可自行取消。若管理員剛建立員工，使用者按右上角重新整理即可更新狀態。

## 建置與驗證

```powershell
dotnet build .\backend\TrainingManagement.Api.csproj
cd frontend
npm run build
cd ..
powershell -ExecutionPolicy Bypass -File .\tests\Invoke-SmokeTest.ps1
```

API 整合測試使用 SQL Server 的獨立 `TrainingManagement_Test_<GUID>` 資料庫及連接埠 5081，測試帳密與金鑰每次隨機產生。僅清理本次建立的測試資料庫，日常資料庫不受影響。結果記錄於 [驗證紀錄](docs/VALIDATION.md)。

## 核心規則

- 僅已發布課程及在職員工可新增報名。
- 自行報名由 JWT 帳號的員工關聯決定，API 不接受客戶端指定其他員工 ID；尚未建立員工資料的帳號不可報名。
- 已取消報名不占名額；同一員工不會重複占用同一課程的名額。
- 一般使用者只能取消自己的「已報名」或「學習中」紀錄；已完訓紀錄不可自行取消。取消後可重新報名，原紀錄會重設為已報名。
- 課程名額使用 SQL Server 交易及課程鎖控制，包括兩人同時搶最後名額。
- 完訓進度為 100%；重新改為未完訓時清除完訓時間。
- 課程及員工若已有訓練紀錄，刪除操作會被阻擋，保留歷史關聯。
- 完訓率及時數由實際訓練紀錄計算，已取消報名不列入。

## API

| 路徑 | 用途 |
| --- | --- |
| `GET /api/health` | 匿名健康檢查 |
| `POST /api/auth/login` | 匿名登入，取得 JWT |
| `POST /api/auth/register` | 匿名註冊一般使用者 |
| `GET /api/auth/me` | 目前登入者 |
| `POST /api/auth/logout` | 撤銷 Token |
| `GET /api/dashboard` | 總覽及統計 |
| `GET, POST /api/courses` | 課程清單及新增 |
| `PUT, DELETE /api/courses/{id}` | 課程編輯及刪除 |
| `GET, POST /api/employees` | 員工清單及新增 |
| `PUT, DELETE /api/employees/{id}` | 員工編輯及刪除 |
| `GET /api/accounts` | 管理員查詢一般使用者帳號與員工建立狀態 |
| `POST /api/accounts/{id}/employee` | 管理員為待處理帳號建立全新員工資料 |
| `GET, POST /api/enrollments` | 報名清單及新增 |
| `GET /api/enrollments/mine` | 一般使用者查看自己的報名 |
| `POST /api/enrollments/self` | 一般使用者以 `{ "courseId": 123 }` 自行報名 |
| `POST /api/enrollments/{id}/cancel` | 一般使用者取消自己的有效報名 |
| `PUT, DELETE /api/enrollments/{id}` | 進度更新及紀錄刪除 |

除健康檢查、登入及註冊外，API 須攜帶 `Authorization: Bearer <token>`；一般使用者僅可新增或取消自己的報名，其他資料寫入須為管理員。錯誤使用 Problem Details：驗證失敗 400、身分驗證失敗 401、權限不足 403、找不到資料 404、重複或名額衝突 409、登入／註冊超過頻率 429。範例見 [API 請求檔](backend/TrainingManagement.Api.http)。

## 目錄

```text
backend/       ASP.NET Core API、SQL Server、EF migrations、JWT
frontend/      Angular、登入、路由驗證、管理畫面與報表
scripts/       本機設定產生、開發啟動及停止腳本
tests/         SQL Server 與 JWT 整合測試
docs/          驗證紀錄與畫面
```
