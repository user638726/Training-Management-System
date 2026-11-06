# 八項進階企業功能

本版延續 [企業功能版本](ENTERPRISE.md) 的 Angular、ASP.NET Core 與 SQL Server 架構。所有新增頁面都可從側欄進入；新資料表使用 EF Core migration 升級，不覆蓋課程、員工、報名或既有證書。

## 1. 多階段簽核與代理

入口：`/workflows`。

- 人資建立簽核流程，設定適用金額門檻與 1–10 個依序處理的關卡。關卡可由所屬部門主管、人資、預算負責人或指定使用者負責。
- 於「課程班次與候補 → 班次與流程設定」將流程連至課程；費用流程則連至年度預算。
- 送出時保存關卡、金額與簽核人。未達流程門檻時沿用原有單階處理。課程費用在流程期間異動時，須撤回重送。
- 代理設定限定明確開始、結束與原因，最長 90 天。一般帳號只可設定自己的代理；Admin 可代為設定。每次決定均記錄實際操作人與被代理者。
- 不能審核自己的申請，不能跳過關卡。已撤銷、停用、過期的代理不生效；簽核人離職、降權或所屬主管／預算負責人已變更時，舊指派不會繼續授權，須由 Admin 處理或重新申請。
- 關卡即將到期會提醒；逾期再通知人資及管理員，不會自動核准。已送出的關卡不會因後續修改流程模板而被覆蓋。

## 2. 課程系列、班次、候補與行事曆

入口：`/sessions`。

- 「課程系列」把不同日期的班次歸在一起。新增班次可複製來源課程設定與當前題庫，先建立為草稿，再到原課程管理確認並發布。
- 新班次預設檢查員工的有效報名時間，重疊則拒絕報名；既有課程可在進階設定明確開啟此檢查。
- 一個班次可設定多個上課場次，場次必須在課程起訖內且不能互相重疊。沒有細分場次時使用課程起訖作為行事曆區間。
- 額滿時可加入候補。取消報名、候補取消或保留到期後，按加入順序釋出名額。
- 保留期限為 24 小時，保留中的席次會計入容量。本人接受前仍會檢查在職、課程狀態、先修、時間與預算；需簽核者會提出申請，必須在保留期限內完成。
- `GET /api/advanced/calendar` 及 `calendar/export` 依登入者資料範圍回傳行事曆及 ICS，不包含已取消報名。
- 名額、候補與預算操作使用 SQL Server 交易和 application lock。學員不會因候補而直接跳過課程簽核。

## 3. 職能矩陣與成長計畫

入口：`/competencies`。

1. 建立職能目錄與各程度的說明，以 1–5 級表示程度。
2. 設定職稱的能力要求，以及課程對應的培養程度。
3. 員工提出自評與實作證據，由所屬主管或人資核對，不能自行核准。
4. 矩陣使用最近一次已核准評量計算差距，並列出對應的已發布課程。
5. 建立個人成長目標、期限與安排課程；只有已核准評量達到目標程度，才能標示完成。

矩陣與評量遵循本人／部門範圍。自評與課程完訓不會自動等同已具備某項能力。

## 4. 年度預算與外訓核銷

入口：`/budgets`。

- 人資設定年度、部門、預算負責人、額度與幣別；不同幣別不自動換算。
- 課程連至預算後，每筆報名依課程每人成本預留額度。年度、部門不符或額度不足時拒絕報名。
- 取消釋放預留，完訓將預留標為認列。核銷對應到同一次報名時，以實際核准支出取代該預留，避免重複計算。
- 費用申請包含訓練機構、金額與 PDF／PNG／JPEG 證明，附件最多 10 MB。
- 可以使用預算的多階流程；未套用流程時由預算負責人或人資審核。本人不能核准自己的核銷或付款紀錄。
- 記錄「已付款」是內部帳務狀態，不會發起銀行轉帳。已有使用紀錄的預算不能任意改變年度、幣別、部門，額度不能低於已用金額。

## 5. 外部證照

入口：`/credentials`。

- 建立證照目錄、職務要求與提前提醒天數。
- 員工上傳證照編號、發證單位、日期與附件，由有資料範圍的主管或人資核對。
- 本人不可核准自己的證照。相同編號不能被不同員工重複認領；同一員工可以續證，連結原有核准紀錄並保存歷史。
- 符合狀態使用已核准、未撤銷且未到期的證照，顯示缺少、即將到期或符合。
- 人資可附理由撤銷已核准證照。背景工作會對即將到期與近期過期的證照提醒。
- 證照審核表示公司已核對申請資料；尚未提供各發證機構的專用查驗連線。

## 6. 人事自動同步與 SCIM

入口：`/provisioning`，限 Admin。

### 人事 CSV 排程

沿用 `Hr__EmployeesEndpoint` 與 `Hr__ApiKey`，來源格式見企業功能說明。每個來源允許一個啟用排程，間隔最少 15 分鐘。排程先完整驗證，再以交易寫入；錯誤、版本衝突或郵件重複會整批失敗並留下紀錄。

自動停權需在排程明確勾選允許；來源未列出的員工維持原狀。自動流程不能停用管理員。可人工立即執行或停用排程。

### SCIM 2.0 Users／Groups 介面

根路徑 `/scim/v2`。先在 `/api-integrations` 建立 `scim.read`／`scim.write` 範圍金鑰，於平台使用 `Authorization: Bearer <金鑰>`。

提供 Users、Groups 的查詢、新增、替換、部分更新及停用／刪除群組，另有 ServiceProviderConfig、ResourceTypes 與 Schemas。查詢支援 `startIndex/count`，每頁最多 500；使用者可用 `userName eq "..."`、`externalId eq "..."`，群組可用 `displayName` 或 `externalId` 的等值篩選。

支援常見的 active、displayName、userName、emails、職稱與企業 extension 部門更新，以及群組 members 的 add／replace／remove（包含 `members[value eq "ID"]`）。未支援的複合 filter、排序、bulk、password、任意 extension 更新會被拒絕。本介面是針對員工帳號的 SCIM 整合範圍，尚未執行完整 SCIM 標準相容性認證或真實 Entra 連線測試。

- 每個金鑰只能管理自己的來源使用者與群組；不會依同名或同郵件接管既有帳號。
- 新使用者建立為 Employee 並產生不可用的隨機本機密碼，不接受遠端指派管理角色。SSO Subject 仍須依公司平台明確對應，不能假設 externalId 與 OIDC subject 相同。
- 群組不會提升角色。管理員在畫面明確對應部門後，群組成員更新才會同步部門。
- 停權會停用帳號與員工、撤銷現有 Token，保留訓練歷史。已升為高權限的帳號拒絕由 SCIM 修改或停用。

## 7. 跨平台學習紀錄

入口：`/learning-records`。

人資建立 HTTPS 外部教材／影音活動，指定所屬課程、活動 IRI、建議學習秒數及可回傳資料的服務帳號。員工可開啟教材及查看紀錄；自填進度標示 SelfReported，外部平台標示 xAPI。

xAPI Statements 閘道：`/api/xapi/statements`。使用具備 `learning.write`／`learning.read` 範圍的金鑰，附上：

```http
Authorization: Bearer <服務金鑰>
X-Experience-API-Version: 1.0.3
Content-Type: application/json
```

```json
{
  "id": "6d00cf31-5a3d-4e59-a89c-e329ff3d0f93",
  "actor": {"objectType": "Agent", "mbox": "mailto:learner@example.com"},
  "verb": {"id": "http://adlnet.gov/expapi/verbs/completed"},
  "object": {"objectType": "Activity", "id": "https://training.example/activity/your-id"},
  "result": {"completion": true, "duration": "PT30M", "score": {"scaled": 0.9}}
}
```

閘道檢查 Actor 對應的在職員工已報名此課程，活動須明確授權該服務帳號。支援單筆／最多 100 筆批次 POST、指定 UUID 的 PUT 與 GET、activity／verb／時間篩選、分頁及本來源 StatementRef 作廢。

相同 ID 與相同內容可重送，不同內容回傳 409。跨來源不能讀取或覆寫紀錄。時間、分數、進度與時長須在介面允許範圍；時長單筆最多一日。

這是有範圍限制的 Statements 閘道，**不是完整 LRS**：尚未提供 Agent Group、多種 Actor 識別、附件傳送、State／Profile 文件 API 或完整 xAPI 相容性認證。若公司平台要求完整 LRS，可將其作為正式 LRS 前的課程紀錄整合層。

外部紀錄或開啟連結不會直接取代本系統的測驗及出席完訓條件。

## 8. 服務帳號與 Webhook

入口：`/api-integrations`，限 Admin。

服務金鑰 1–365 天有效，只有建立／輪替時顯示原值；資料庫只保存 SHA-256 雜湊。可立即撤銷，輪替會使舊金鑰失效。授權範圍為 `scim.read`、`scim.write`、`learning.read`、`learning.write`、`training.read`。`GET /api/integrations/courses` 提供已發布課程的分頁目錄，不包含員工資料；服務金鑰不能存取一般管理 API。

Webhook 可訂閱 `course.published`、`course.updated`、`enrollment.registered`、`enrollment.approved`、`enrollment.cancelled`、`enrollment.completed`。事件與原業務資料在同一資料庫交易保存，再由背景工作展開寄送；事件只帶必要識別碼及狀態，不傳送密碼或金鑰。

接收端須檢查：

```text
X-Training-Event-Id: <固定事件 UUID>
X-Training-Timestamp: <Unix 秒數>
X-Training-Signature: v1=<小寫十六進位 HMAC-SHA256>
簽章輸入：timestamp + "." + 原始 HTTP body
簽章金鑰：畫面顯示的秘密字串，以 UTF-8 編碼
```

建議拒絕超過五分鐘的時間戳記，依事件 ID 去重，驗證簽章後才執行業務操作。每次重試使用相同事件 ID、相同內容與新的時間戳記。HTTP 2xx 為成功，其他結果逐次延後重試，最多五次後進入人工處理；畫面提供每次寄送紀錄與重新排送。

目的地必須使用 HTTPS，拒絕重新導向、私人及本機位址，連線時再次解析並檢查 IP。秘密使用 Data Protection 加密保存，多執行個體須共用受保護且持久化的金鑰。傳送採至少一次語意，接收端需處理重複事件；停用不會撤回已在傳送中的請求。

僅隔離測試環境使用 `Integrations__AllowLoopbackWebhooks=true` 允許本機測試接收器，正式環境不接受此例外。

## 升級、驗證與限制

- 新增 `AdvancedBusinessFeatures` 與 `AdvancedIntegrityGuards` migrations。開發啟動會自動升級；正式環境先備份，再審查發布包 `migrate.sql`。
- 所有 SQL datetime2 時間統一輸出 UTC 的 `Z`，畫面依使用者時區顯示。行事曆以 UTC 匯出。
- 新表單選擇器提供伺服器搜尋及每批 30 筆的更多選項；矩陣支援伺服器分頁。部分管理清單仍限制最近 500／1000 筆，超大資料量部署前仍需擴充完整歷史清單分頁及壓測。
- 郵件、人事來源、公司 SCIM／SSO、外部教材與真實 Webhook 接收端須另外提供設定並在公司環境驗收；本機測試不會連線到公司的系統。

```powershell
dotnet build .\backend\TrainingManagement.Api.csproj --configuration Release
npm --prefix frontend run build
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Invoke-SmokeTest.ps1 -Configuration Release -NoBuild
```

測試使用獨立 SQL Server 資料庫、loopback 人事／Webhook 測試接收器與隨機服務金鑰，完成後清理本次程序及資料庫。正式服務不會因測試資料而變更。
