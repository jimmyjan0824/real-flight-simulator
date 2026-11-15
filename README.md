# Real Flight Simulator（MVP）

原創 iOS 飛行模擬遊戲，主打「危險機場／極端天氣」短時挑戰。Unity 6 + C#，**Code-First 架構**：主場景裡只有一個 `GameBootstrapper`，相機、燈光、地面、跑道全部在執行時由程式產生。

## 專案結構

```
Assets/
  Scripts/GameBootstrapper.cs   執行時建立相機、太陽光、地面、跑道（含中心線），左上角顯示 FPS
  Editor/BuildScript.cs         iOS 設定 + 命令列打包（BuildScript.BuildIOS）
                                 選單：FlightSim > Create Or Reset Main Scene / Apply iOS Player Settings / Build iOS
  Scenes/Main.unity             （第一次打包時由 BuildScript 自動產生，不用手動建）
Packages/manifest.json          只用到內建模組，Unity 會自動補齊其餘套件
ProjectSettings/                目前只放 ProjectVersion.txt，其餘由 Unity 第一次開啟時產生
scripts/build_ios.sh            命令列打包成 Xcode 專案
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
   > 已經裝好但忘了勾：Installs → 該版本右邊齒輪 → Add modules → iOS Build Support。

### 2. 取得專案

```bash
cd ~/Projects            # 或你習慣的資料夾
git clone https://github.com/jimmyjan0824/real-flight-simulator.git
cd real-flight-simulator
```

### 3. 第一次用 Unity 開啟

1. Unity Hub → **Projects** → **Add** → **Add project from disk** → 選 `real-flight-simulator` 資料夾。
2. 專案裡的 `ProjectVersion.txt` 是佔位版本號，Hub 很可能顯示「版本未安裝」。點版本欄，**改選你剛裝的 Unity 6 LTS**，若跳出升級/更換版本確認就按 **Continue / Change Version**。
3. 第一次開啟會匯入並產生 `Library/` 和 `ProjectSettings/`，需要幾分鐘。
4. 開好後，請確認：
   - 下方 **Console 沒有紅色錯誤**。
   - 上方選單出現 **FlightSim**。
   - 點 **FlightSim → Create Or Reset Main Scene**，會產生 `Assets/Scenes/Main.unity` 並自動開啟。按上方 ▶ Play，應該看到藍天、綠地、灰色跑道和白色中心線，鏡頭慢慢繞著跑道轉，左上角顯示 FPS。
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
