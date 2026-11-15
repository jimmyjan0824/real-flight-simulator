# Real Flight Simulator（MVP）

原創 iOS 飛行模擬遊戲，主打「危險機場／極端天氣」短時挑戰。Unity 6 + C#，**Code-First 架構**：主場景裡只有一個 `GameBootstrapper`，相機、燈光、地面、跑道全部在執行時由程式產生。

## 專案結構

```
Assets/
  Scripts/GameBootstrapper.cs        進入點：執行時建立世界、飛機、相機、操控、HUD
  Scripts/Flight/AircraftController.cs  飛行物理（升力、阻力、推力、重力、三軸力矩、襟翼、配平）
  Scripts/Flight/LandingGear.cs      射線式起落架（避震彈簧、煞車、前輪轉向）
  Scripts/Flight/AircraftBuilder.cs  用方塊組出飛機（含碰撞體，用於墜毀判定）
  Scripts/Flight/LandingJudge.cs     起飛／接地率 FPM／彈跳／停止／墜毀判定
  Scripts/Controls/FlightInput.cs    觸控搖桿、傾斜操控、油門、按鈕、鍵盤（Editor 測試用）
  Scripts/CameraRig/FlightCamera.cs  第三人稱追蹤／機頭（座艙）兩種視角
  Scripts/UI/FlightHud.cs, GuiDraw.cs  2D HUD（空速、高度、升降率、航向、姿態儀）
  Scripts/World/Runway.cs, Rendering/Materials.cs  跑道資料、不會被剔除的材質
  Editor/BuildScript.cs              iOS 設定 + 命令列打包（BuildScript.BuildIOS）
                                     選單：FlightSim > Create Or Reset Main Scene / Apply iOS Player Settings / Build iOS
  Scenes/Main.unity                  只有一個 GameBootstrapper
Packages/manifest.json               只用到內建模組
ProjectSettings/                     Unity 產生的設定（Active Input Handling = 舊版 Input Manager）
scripts/build_ios.sh                 命令列打包成 Xcode 專案
```
iOS 相關設定（Bundle ID、最低 iOS 15、IL2CPP、只允許橫向、自動簽署）都寫在 `BuildScript.ApplyIOSSettings()`，每次打包都會重新套用，所以不用手改 ProjectSettings 的 YAML。

---

## 階段 0：在你的 Mac 上一步一步完成

完成標準：**一個空場景（天空、草地、跑道、緩慢繞圈的鏡頭）能裝進你的 iPhone 執行。**

### 1. 安裝工具（約 1–2 小時，大多是下載時間）

1. **Xcode**：從 Mac App Store 安裝最新版，裝好後打開一次，同意授權並等它裝完附加元件。
   接著在終端機執行：
   ```bash
   xcode-select --install          # 若已安裝會提示已存在，沒關係
   sudo xcode-select -s /Applications/Xcode.app
   ```
2. **Git LFS**（大型美術素材用）：
   ```bash
   brew install git-lfs
   git lfs install
   ```
3. **Unity Hub**：到 <https://unity.com/download> 下載安裝，用 Unity 帳號登入，選 **Personal（免費）** 授權。
4. **Unity 6 LTS**：Unity Hub → **Installs** → **Install Editor** → 選最新的 **Unity 6 LTS（6000.x，標示 LTS）**。
   在模組清單中**務必勾選 `iOS Build Support`**（Android、WebGL 等先不用勾）。
   > 已經裝好但忘了勾：Installs → 滑鼠移到該版本那一列 → 最右邊出現的 **⋮**（三個點）→ **Add modules** → 勾 iOS Build Support → Install。
   > 若 ⋮ 選單裡沒有 Add modules，代表該版本不是用 Hub 安裝的，請改用 Hub 的 **Install Editor** 重新安裝 Unity 6 LTS 並勾選 iOS Build Support。
   > 確認方式：`/Applications/Unity/Hub/Editor/<版本>/PlaybackEngines/iOSSupport` 資料夾存在即可。

### 2. 取得專案

```bash
cd ~/Projects            # 或你習慣的資料夾
git clone https://github.com/jimmyjan0824/real-flight-simulator.git
cd real-flight-simulator
```

### 3. 第一次用 Unity 開啟

1. Unity Hub → **Projects** → **Add** → **Add project from disk** → 選 `real-flight-simulator` 資料夾。
2. 專案裡的 `ProjectVersion.txt` 是佔位版本號（6000.0.58f1），Hub 可能顯示「版本未安裝」或跳出「安裝 Unity 6.0 (6000.0.58f1)」視窗並帶有**安全性警訊**。
   **不要安裝這個舊版**，直接關掉視窗。請確認你已在 Installs 裝好**最新的 Unity 6 LTS**（Hub 裡沒有安全性警訊標籤的那個），然後在 Projects 清單點該專案的「Editor 版本」欄位，**改選你裝好的 Unity 6 LTS**，若跳出升級/更換版本確認就按 **Continue / Change Version**。開啟後 Unity 會把 `ProjectVersion.txt` 改成你的版本，請一起 commit。
3. 第一次開啟會匯入並產生 `Library/` 和 `ProjectSettings/`，需要幾分鐘。
4. 開好後，請確認：
   - 下方 **Console 沒有紅色錯誤**。
   - 上方選單出現 **FlightSim**。
   - 點 **FlightSim → Create Or Reset Main Scene**，會產生 `Assets/Scenes/Main.unity` 並自動開啟。此時 Scene 分頁是空的（只有 GameBootstrapper）是正常的，因為所有物件都在執行時才產生。按上方 ▶ Play 並切到 **Game** 分頁，應該看到藍天、綠地、灰色跑道和白色中心線，鏡頭慢慢繞著跑道轉，左上角顯示 FPS。
5. **關閉 Unity Editor**（命令列打包時，同一個專案不能同時被 Editor 開著）。
6. 把 Unity 產生的檔案 commit 起來（`.meta` 檔一定要 commit）：
   ```bash
   git checkout -b chore/unity-generated
   git add -A
   git commit -m "Add Unity-generated ProjectSettings, meta files and Main scene"
   git push -u origin chore/unity-generated
   ```

### 4. 修改 Bundle ID（只要做一次）

免費的 Personal Team 要求 Bundle ID 全世界唯一。打開 `Assets/Editor/BuildScript.cs`，把
```csharp
private const string DefaultBundleId = "com.jimmyjan.flightsimmvp";
```
改成你自己的，例如 `com.<你的英文名>.flightsimmvp`（只用小寫英文、數字、點）。
也可以不改程式，打包時用環境變數臨時覆蓋：`BUNDLE_ID=com.xxx.flightsim ./scripts/build_ios.sh`

### 5. 命令列打包成 Xcode 專案

```bash
./scripts/build_ios.sh
```
- 腳本會自動找 `/Applications/Unity/Hub/Editor/` 底下最新的 6000.x 版本；找不到時可指定：
  `UNITY_PATH="/Applications/Unity/Hub/Editor/6000.x.xxf1/Unity.app/Contents/MacOS/Unity" ./scripts/build_ios.sh`
- 第一次會比較久（5–15 分鐘）。完整 log 在 `build/unity_build_ios.log`，失敗時終端機會印出最後 40 行。
- 成功後會印出 `BUILD OK` 並自動開啟 `build/iOS/Unity-iPhone.xcodeproj`。

### 6. Xcode 簽署（免費 Personal Team）

1. Xcode → **Settings…（⌘,）→ Accounts** → 左下 **+** → **Apple ID**，登入你的 Apple ID。登入後會自動出現 「(你的名字) (Personal Team)」。
2. 左側點最上面的 **Unity-iPhone** 專案 → TARGETS 選 **Unity-iPhone** → **Signing & Capabilities**：
   - 勾選 **Automatically manage signing**
   - **Team** 選 「(你的名字) (Personal Team)」
   - 若出現 Bundle Identifier 已被使用的錯誤，回到步驟 4 換一個 Bundle ID，重新打包。
3. 若 TARGETS 裡還有 **UnityFramework**，它通常不需要另外設定；若出現簽署錯誤，同樣把 Team 設為你的 Personal Team。

> 小技巧：在 Signing 畫面可以看到 Team ID（10 碼）。之後執行
> `IOS_TEAM_ID=XXXXXXXXXX ./scripts/build_ios.sh` 就會自動帶入，不用每次在 Xcode 選。

### 7. iPhone 準備

1. 用傳輸線把 iPhone 接上 Mac，iPhone 上按 **信任這部電腦**。
2. 開啟**開發者模式**：iPhone **設定 → 隱私權與安全性 → 開發者模式** → 打開 → 重新開機 → 開機後再確認一次「開啟」。
   > 如果找不到「開發者模式」，先讓 Xcode 認到手機一次（Xcode 上方裝置選單選你的 iPhone），選項就會出現。

### 8. Run！

1. Xcode 上方裝置選單選你的 **iPhone**（不是模擬器）。
2. 按 **▶ Run（⌘R）**。第一次 Xcode 會準備裝置符號檔，可能要幾分鐘。
3. 第一次安裝後 App 可能打不開，提示「未受信任的開發者」：
   iPhone **設定 → 一般 → VPN 與裝置管理** → 點你的 Apple ID（開發者 App）→ **信任**。
4. 再按一次 Run，或直接點手機上的 App 圖示。

✅ 看到藍天、草地、跑道、鏡頭繞圈、左上角 FPS → **階段 0 完成！**

---

## 階段 1：飛行原型（完成標準：在 iPhone 上起飛、繞場、降落）

### 畫面與操控（橫向）

| 位置 | 控制 | 說明 |
| --- | --- | --- |
| 左邊直條 | **THR 油門** | 手指上下拖曳 |
| 左側欄 | **TRIM UP / TRIM DN** | 按住調整升降舵配平（UP = 機頭上仰），讓你不用一直拉著搖桿 |
| 左側欄 | **FLAPS** | 點一下切換襟翼 0° → 10° → 30° |
| 左側欄 | **BRAKE** | 按住煞車（地面） |
| 右半邊 | **虛擬搖桿**（觸控模式） | 在右半邊任何地方按下拖曳：左右 = 滾轉，**往下拉 = 機頭上仰**，往上推 = 機頭下俯。在地面時左右也會轉動前輪 |
| 右上 | **CTRL: TOUCH / TILT** | 切換觸控／傾斜。傾斜模式像方向盤一樣轉手機來滾轉，手機上緣往自己傾 = 拉起。切到傾斜時會以當下姿勢為中立點，可再按 **CENTER** 重設 |
| 右上 | **VIEW** | 第三人稱 ↔ 機頭（座艙）視角 |
| 右上 | **APPROACH** | 直接出現在跑道頭外 3.3 km、3° 下滑道上（約 70 kt，襟翼 10°），練降落用 |
| 右上 | **RESET** | 回到跑道頭 |

HUD：左上是 IAS（空速，節）、ALT（高度，呎）、V/S（升降率，呎/分）、HDG（航向，跑道方向是 000）；下方中間是姿態儀（地平線、俯仰刻度、坡度指標）。失速時會閃紅色 **STALL**。

接地評分（以接地前一刻的下沉率 FPM 計算）：< 120 **BUTTER!**、< 300 **SMOOTH**、< 500 **FIRM**、< 800 **HARD**、≥ 800 起落架損壞（墜毀）。另外會標示前輪先著地、單翼過低、偏離跑道、彈跳次數；完全停止後會顯示接地點離跑道頭距離與偏離中心線多少公尺。翼尖或螺旋槳擦地、機身重撞也會判定墜毀。

> 遊戲內文字暫時用英文（Unity 預設字型在 iOS 上不保證有中文字），中文字型會在之後的階段加入。

### 飛行小抄（第一次飛照這個做）

1. **起飛**：油門推到底 → 保持在中心線（搖桿左右微調前輪）→ 空速到 **55 kt** 輕輕往下拉搖桿 → 機頭抬起約 8–10° 就放鬆，讓它自己爬升。不要拉太猛，超過約 12° 會擦機尾。
2. **爬升**：維持約 70–80 kt 爬到 **800–1000 ft**，油門可收到 70–80%。按 TRIM UP/DN 調到放手也能大致平飛。
3. **繞場**：轉彎坡度 15–25° 就好，邊轉邊稍微拉一點維持高度。四個 90° 左轉或右轉繞回來，和跑道平行飛一段（下風邊），再轉兩次對準跑道。跑道頭有白色斑馬線，離跑道頭 300 m 有兩塊大白色瞄準點。
4. **進場**：放襟翼 30°，油門約 20–30%，空速 **60–70 kt**，下沉率約 **300–500 fpm**，對準中心線瞄準白色瞄準點。
5. **落地**：離地約 10–20 ft（AGL）時收油門到 0，輕輕往下拉讓機頭微抬（拉平），讓下沉率降到 **300 fpm 以下**再讓主輪接地 → 放鬆搖桿讓前輪落地 → 按住 BRAKE 停下。

想直接練降落：按 **APPROACH**。

### 在你的 Mac 上測試

1. 取得這個分支：
   ```bash
   cd ~/Projects/real-flight-simulator
   git fetch origin
   git checkout stage1/flight-prototype
   ```
2. **先在 Unity Editor 試飛（建議）**：用 Unity Hub 開啟專案 → 等匯入完成，確認 Console 沒有紅字 → 開 `Assets/Scenes/Main.unity` → 按 ▶ Play。
   Editor 裡可用滑鼠當一根手指，也可以用鍵盤：W/S 或 ↑/↓ 俯仰（S = 拉起）、A/D 或 ←/→ 滾轉、Q/E 方向舵、Left Shift / Left Ctrl 油門、F 襟翼、B 煞車、[ ] 配平、V 視角、M 切換操控、R 重設、T 進場。
   Game 分頁上方的解析度建議選 **1920x1080** 或其他橫向比例，版面才會跟手機一樣。
3. Unity 會為新的 `.cs` 檔與資料夾產生 `.meta` 檔，請一起 commit：
   ```bash
   git add -A && git commit -m "Add Unity meta files for Stage 1 scripts" && git push
   ```
4. **關閉 Unity Editor**，打包並裝到 iPhone（跟階段 0 一樣）：
   ```bash
   ./scripts/build_ios.sh
   ```
   Xcode 打開後選你的 iPhone → ▶ Run。（Personal Team 簽署 7 天會過期，過期就重新 Run 一次。）

### iPhone 驗收清單

- [ ] App 開啟後看到飛機停在跑道頭，畫面有 HUD、油門條、按鈕，FPS 大約 60
- [ ] 油門推上去飛機會加速，螺旋槳轉速變快；搖桿左右能讓飛機在跑道上轉向
- [ ] 55–60 kt 拉起能離地，畫面出現 **TAKEOFF**
- [ ] 空中滾轉、俯仰正常，方向與直覺一致（往下拉 = 機頭上仰）
- [ ] 拉太多會出現 **STALL** 並掉高度，放鬆搖桿能改出
- [ ] VIEW 可切換第三人稱 ↔ 機頭視角
- [ ] CTRL 切到 TILT 後轉手機可以操控，CENTER 能重設中立
- [ ] 繞場一圈回到跑道並落地，畫面顯示評分與 FPM；停下後顯示 **LANDED** 與接地資訊
- [ ] APPROACH 可直接練進場；用力撞地會顯示墜毀，RESET 能重來

測試時請記下：手感太敏感／太鈍、哪裡不直覺、FPS 有沒有掉、任何錯誤畫面（Xcode 下方 Console 的紅字可以直接貼給 Claude）。

---

## 免費 Personal Team 的限制（先知道就好）

- 裝到手機上的 App **7 天後會失效**，重新從 Xcode Run 一次即可。
- 同時最多 3 台裝置、每週可建立的 App ID 數量有限制。
- 不能上架 App Store／TestFlight。這些要等階段 4 再買 Apple Developer Program（年費 US$99）。

## 常見問題

| 狀況 | 處理 |
| --- | --- |
| `build_ios.sh` 說找不到 Unity 6 | 確認 Hub 安裝路徑是 `/Applications/Unity/Hub/Editor/`，或用 `UNITY_PATH` 指定 |
| log 出現 `iOS Build Support` / `Could not switch to iOS` | Hub 補裝 iOS Build Support 模組 |
| `It looks like another Unity instance is running with this project open` | 先關掉 Unity Editor 再跑腳本 |
| Xcode：`Failed to register bundle identifier` | Bundle ID 被用過了，換一個（步驟 4） |
| Xcode：`Developer Mode disabled` | 依步驟 7 開啟開發者模式 |
| App 一打開就閃退 | 在 Xcode 下方 Console 看錯誤訊息，貼給 Claude |
| 畫面全黑或全粉紅 | 粉紅代表 shader 被剔除，把 Xcode Console 訊息貼給 Claude |

## 授權與素材

本專案為原創作品。未來若參考開源專案（例如 vazgriz/FlightSimulator），必須逐一確認每個素材的授權後才能使用，且上架前需全部替換為自有素材。
