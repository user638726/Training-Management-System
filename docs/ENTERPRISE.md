# 企業功能操作與部署

多階簽核、班次候補、職能、預算核銷、外部證照、SCIM／人事同步、xAPI 與 Webhook 的操作見 [八項進階功能說明](ADVANCED.md)。

本版以單一企業內部訓練管理為範圍。外部 SSO、SMTP 與人事系統已提供可設定介面，預設不連線、不寄信；需由公司提供實際設定後完成整合驗收。多租戶、計費與組織外公開課程不包含在本版。

## 功能與入口

| 功能 | 入口 | 可執行角色 |
|---|---|---|
| 部門、上下層組織與主管範圍 | `/organization` | Admin、HR |
| 核准註冊、角色、停權、既有員工綁定、公司邀請 | `/accounts` | Admin |
| 必修計畫、部門／職務／新人條件、批次指派、複訓 | `/plans` | Admin、HR |
| 個人任務、教材、測驗、問卷、電子證書 | `/learning` | 已登入使用者；學習行為需綁定員工 |
| 報名申請、核准、退回、撤回與簽核歷程 | `/approvals` | 本人查詢／撤回；Admin、HR、所屬主管簽核 |
| 講師指派、先修、完訓規則、訓練成本、教材、題庫與出席 | `/materials` | Admin、HR；Instructor 僅可管理指派給自己的課程教材、題庫與出席 |
| 站內通知、已讀與郵件寄送狀態 | `/notifications` | 本人 |
| 必修達成率、逾期、即將到期證書、成本、滿意度、定期報表 | `/management-reports` | 依資料範圍；訂閱需 Admin、HR、Manager |
| 不可由應用程式修改的操作前後紀錄 | `/audit` | Admin |
| CSV 預覽、批次新增／更新、離職停權、人事來源預覽 | `/imports` | Admin、HR |
| 整合設定狀態、資料庫狀態、請求／錯誤數、排程心跳 | `/integrations` | Admin |
| 變更密碼並撤銷所有既有登入 | `/settings` | 本人 |

## 建議操作順序

1. 由管理員在帳號管理核准註冊，綁定已核對身分的既有員工。亦可在原員工管理頁為帳號新增員工。
2. 指定 HR、Manager、Instructor 角色，再於組織頁指定各部門的主管及上下層關係。
3. 建立課程，至教材頁指定講師、簽核／出席／測驗條件、及格分數、先修課、每人成本與證書有效月數。
4. 指定講師上傳 PDF 或 UTF-8 文字教材、編輯選擇題並發布。教材最多 10 MB，每份測驗最多 100 題，每題 2–6 個選項。
5. HR 建立學習計畫，選擇課程與適用員工條件，設定期限與複訓週期。可立即指派，也可等待每分鐘的背景工作。
6. 員工於學習中心查看任務並報名；需簽核的課程先送申請。主管核准後才檢查並占用名額，不能核准自己的申請。
7. 學員作答由後端評分；講師確認出席。通過要求後自動完訓並核發可下載、列印的 HTML 證書；也可由 HR 依規則維護進度。
8. 主管查看報表、訂閱每週／每月報表，從通知及報表頁下載已產生的 CSV。

## 權限與歷史規則

- Admin、HR 具有全公司訓練資料範圍；Manager 為所負責部門及所有下層部門，另可查看自己的資料；Instructor 為自己授課的學員及自己的資料；Employee 僅本人。
- 權限在後端清單、單筆讀取、報表、下載與修改時檢查。課程目錄對一般使用者僅顯示已發布、自己已報名或自己負責的課程。前端隱藏選單不取代 API 授權。
- 角色／核准／帳號狀態／員工關聯異動會撤銷舊 Token。員工停用與匯入離職資料也會停用其帳號。
- 企業註冊預設需核准，邀請接受後為已核准帳號。邀請三天有效、僅能使用一次；連結由管理員自行交付，不會自動寄送。
- 完訓時保存認列時數、每人成本、課程名稱、部門及課程版本；後續課程編輯不回寫歷史。
- 重新開啟完訓紀錄會保存前一梯次證據、撤銷其證書並增加報名梯次；取消後重報也保留舊梯次，不覆蓋舊證據。
- 訓練紀錄不可直接刪除，`DELETE /api/enrollments/{id}` 回傳 409，請使用取消。已有關聯的員工及課程也不能直接刪除。
- 題庫另有版本，修改課程名稱或時間不會清空題庫；新測驗版本保留舊題目與作答證據。學員不會取得正解。每次作答均保存，當梯次保留最高分。
- 編輯介面使用 SQL Server rowversion 偵測衝突，收到 409 後重新載入再編輯。既有核心 PUT API 為相容原用戶端仍接受省略版本，企業管理 API／本版前端則傳送版本。
- 報表統計排除已取消報名；CSV 明細可包含取消紀錄，並支援狀態篩選。報表日期依完訓時間、未完訓則依報名時間，結束時間為不含該時刻的上界。畫面日期結束日會轉成隔日零時。
- 定期報表為最近 30 天的 CSV 快照，僅原訂閱者可下載；若其可查看員工範圍已變更，會拒絕下載舊快照，須重新產生。
- 舊資料 migration 保留帳號核准與核心資料，為既有完訓補齊時數快照與證書。舊員工到職日未知時保留最早日期，請透過員工編輯或匯入補正，不會被當作新人。

## 匯入與人事介面

CSV 使用 UTF-8，可帶 BOM、引號、逗號及欄位內換行：

```csv
employeeNumber,name,email,department,jobTitle,isActive,hiredAt
EMP-001,王小明,person@example.com,人資部,專員,true,2026-01-01
```

`hiredAt` 可省略，格式為 `yyyy-MM-dd`；`isActive` 必須為 `true` 或 `false`。每次 1–1000 筆，來源上限 2 MB。系統以員工編號比對，驗證重複編號、郵件、欄位與日期；任何錯誤都不會部分寫入。預覽批次綁定建立者，30 分鐘到期，確認時再次檢查資料版本與唯一性；資料被別人更新後需重新預覽。

人事系統設定 `Hr__EmployeesEndpoint` 為回傳相同 CSV 的 HTTPS GET URL，選配 `Hr__ApiKey`（以 `X-Api-Key` 傳送）。匯入頁的「從人事系統取得預覽」只取得資料，仍需明確確認才寫入。連線 15 秒逾時，拒絕重新導向。未提供任何指定人事產品的專用欄位轉換器。

## SSO 與郵件

設定名稱見 `backend/enterprise.env.example`。此範例不會自動載入，請透過環境變數或部署平台的秘密管理注入。

SSO 使用 OIDC discovery、授權碼、PKCE、nonce、五分鐘狀態 Cookie、ID Token 簽章／issuer／audience／期限驗證，以及一分鐘且僅能使用一次的登入交換碼。管理員先綁定帳號的外部 Subject；issuer 取自 `Sso__Issuer`，未指定時取 `Sso__Authority`，必須與 discovery 的 issuer 完全一致。變更身分平台後需重新核對綁定；不會憑電子郵件自動建立高權限帳號。公司 MFA 應由身分平台強制執行。

在身分平台註冊回呼 URL：`https://你的網站/api/auth/sso/callback`。開發環境僅允許 loopback 使用 HTTP。多個 API 執行個體須共用同一個持久化 Data Protection 金鑰目錄及應用名稱，並限制作業系統存取權；目前金鑰保護依賴儲存環境，正式環境應使用受保護磁碟／磁碟加密或自行配置金鑰加密。

SMTP 啟用前只建立站內通知。啟用後使用 STARTTLS，最多重試三次，失敗狀態可由通知與維運頁檢查；無已綁定員工郵件時標示 NoAddress。寄送採至少一次語意，服務在 SMTP 接受後、資料庫提交前中斷，可能造成重寄。真實 SSO、MFA、SMTP 及人事連線須在公司環境另外驗收。

## 分頁與容量

核心清單支援 `page`、`pageSize`、`search`，每頁上限 500，回傳 `X-Total-Count`。另有課程 `category/status`、員工 `department/isActive`、報名 `department/status/courseId` 篩選。既有畫面每頁 20 筆，儀表板與主管彙總由資料庫計算。跨網域部署須在 CORS 設定公開 `X-Total-Count`。

目前企業頁面選擇器最多載入 500 筆核心資料，任務／證書清單最多 1000 筆、簽核 500 筆、個人通知 200 筆；這些頁面適合本專案目前規模。部署到更大企業前需擴充這些選擇器與企業清單的搜尋／分頁，並依正式資料量壓測。CSV 匯出超過十萬筆會要求縮小範圍。

## 發布、維運與復原

```powershell
.\scripts\Publish-Release.ps1
# 已安裝前端依賴時可使用 -SkipInstall
```

發布包位於 `.runtime/releases/<UTC時間>/`，包含 ASP.NET Core Release、Angular 靜態網頁及冪等 `migrate.sql`。正式環境先備份並審查／執行 migration，再啟動；`Database__AutoMigrate=false` 時若結構未升級會拒絕啟動。Development 預設自動升級。

正式部署需配置有效 TLS 憑證、外部主機名稱、CORS 白名單、連線及 JWT 秘密、持久化金鑰目錄。ASP.NET Core 提供 HSTS、HTTPS 重新導向及基本安全回應標頭；反向代理應正確設定 HTTPS forwarding，勿將任意來源的 forwarded headers 視為可信。資料庫應採最小權限帳號；稽核保存期限、備份保留與日誌集中化須依公司政策設定。

- `/api/health`：程序存活。
- `/api/health/ready`：資料庫可連線，失敗回傳 503。
- `/api/enterprise/operations`：Admin 可讀取資料庫狀態、排程成功時間、失敗通知與請求／5xx 數。
- `.NET Meter` 名稱 `TrainingManagement.Api`，提供 `training.http.requests` 及 `training.http.duration`，可由部署環境接入 .NET diagnostics／OpenTelemetry 收集器。
- `POST /api/enterprise/operations/run`：Admin 立即執行指派、提醒及報表，不執行郵件寄送。背景工作每分鐘執行，SQL application lock 避免多執行個體重複排程。

備份目錄必須在 SQL Server 主機且可由 SQL Server 服務帳號寫入；桌面專案路徑通常不符合此條件：

```powershell
.\scripts\Backup-Database.ps1 -ServerBackupDirectory 'C:\SQLBackups'
.\scripts\Test-Restore.ps1 -ServerBackupFile 'C:\SQLBackups\你的備份.bak' `
  -TargetDatabase 'TrainingManagement_RestoreTest_20261106' -ServerDataDirectory 'C:\SQLData'
```

還原演練只接受新的 `TrainingManagement_RestoreTest_` 資料庫，不使用 REPLACE、不覆寫日常資料庫、不自動刪除結果。若備份的邏輯檔名不是預設資料庫名與 `_log`，先執行 `RESTORE FILELISTONLY` 核對並調整腳本。

`.github/workflows/validate.yml` 提供 Windows CI 建置及隔離 SQL LocalDB 整合測試。執行器需有 SQL LocalDB 與 sqlcmd；尚未在遠端 CI 服務執行。

## 驗證

```powershell
dotnet build .\backend\TrainingManagement.Api.csproj
npm --prefix frontend run build
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Invoke-SmokeTest.ps1
```

整合測試每次建立獨立 SQL Server 資料庫，通過後只清理該次測試資料庫。失敗時保留資料與日誌供調查。涵蓋原功能及企業角色／範圍、版本衝突、簽核、任務冪等、測驗／出席、歷史時數／成本、證書撤銷、問卷、通知、排程、匯入、檔案及註冊核准。
