# LocaForge 汉化工坊

电脑版游戏汉化补丁的安装工具和补丁发布仓库。目前支持：

| 游戏 | 版本 | 引擎 | 补丁方式 |
|---|---|---|---|
| FMGWorld | 0.2.1 | Ren'Py | 覆盖 `game` 目录里的脚本和字体 |
| 万物生长领域 Realm of Evergrow | 0.3.4.2 | Godot 4（单文件） | 外挂补丁，exe 不改 |

![界面](docs/screenshot.png)

## 下载使用

1. 到 [Releases](../../releases/latest) 下载 `LocaForge.zip`，解压到任意位置
2. 双击 `LocaForge.exe`，工具会自动查找电脑上的游戏；找不到时点"搜索电脑"，或把游戏文件夹拖进窗口
3. 本机版本和补丁适配一致后，点"安装中文补丁"
4. 想恢复英文原版时，点"还原原版"

工具每次启动会自动检查新版补丁和新版工具，有更新时点"立即更新"即可，不用重新下载。

在游戏列表里右键（或选中后按 Delete）可以把不想看到的目录从列表中移除，只是不再显示，不会删除任何文件；以后手动选择该目录即可恢复。

左下角提供“问题反馈”和“提交想汉化的游戏”，分别打开本项目的 GitHub 表单。汉化申请只需填写游戏名称、资源地址，附件可选；附上资源可以让汉化更快，适配更准。用户需登录 GitHub 后自行提交，工具不会自动发送报告。

安装前会把被覆盖的原文件备份到游戏目录的 `_zh_patch` 文件夹，还原时原样放回。补丁不修改游戏程序，也不动存档。

非官方汉化，需自备正版，仅供学习交流，版权归原开发者所有。原游戏含成人内容，仅限 18 岁以上使用。使用前请阅读发布包中的《免责声明》。

---

## 开发

C# WinForms，目标 .NET Framework 4.8（Win10/11 自带），64 位运行，exe 双击即用。界面配色与安卓版汉化工具一致。

### 构建

```powershell
.\build.ps1
```

产物在 `publish\`：`LocaForge.exe`、`catalog.json`、`payload\fmgworld-zh-0.2.1.zip`。
补丁包由 `..\FMGWorld-汉化补丁-0.2.1\game` 打包而来，更新译文后重新构建即可。

### 发布新版本

```powershell
.\release.ps1 -Notes "这次改了什么"            # 构建、签名、创建 Release 并上传
.\release.ps1 -Notes "……" -DryRun             # 只在 build\release-out 生成文件，不上传
```

- **发补丁更新**：改完译文后，把 `catalog.json` 的 `revision` +1，对应 `patches[].patchVersion` 改新
- **发工具更新**：把 `LocaForge.csproj` 的 `<Version>` 改新
- 两者可以一起发。每个 Release 都带完整的工具包和一份签名的 `update.json`，工具从 `releases/latest` 读取它

### 自动更新怎么工作

1. 工具读取 `releases/latest/download/update.json` 和 `update.json.sig`，用内置公钥（`src/Core/UpdateKey.cs`）验证签名，不通过就忽略
2. 清单里的补丁 `revision` 比本地大：下载补丁包到 `%LOCALAPPDATA%\LocaForge\updates`，逐个校验大小和 SHA-256，全部通过后才写入新的 `catalog.json`
3. 清单里的工具版本比本地新：下载新 exe 并校验，把正在运行的 exe 改名为 `.old`，新 exe 放回原名后重新打开
4. 已装过旧补丁的游戏会显示"可更新"，点"更新汉化补丁"即先还原再装新版

**签名私钥** 在 `signing\update-signing.private.xml`，不在仓库里，请另行离线备份。丢失后已发出的工具无法再接受更新，只能让用户手动下载新版。

### 结构

| 文件 | 作用 |
|---|---|
| `catalog.json` | 支持的游戏、版本指纹、版本对应的补丁包 |
| `src/Core/GameLocator.cs` | 找游戏：Steam 库 → itch → Windows 运行记录 → 磁盘扫描；也接受手动选择和拖放 |
| `src/Core/GameInspector.cs` | 按 `game/scripts.rpa` 的指纹识别版本、判断是否已汉化、检测游戏是否在运行 |
| `src/Core/PatchEngine.cs` | 安装 / 还原：备份 → 临时文件替换 → 哈希校验；失败自动回滚 |
| `src/Core/UpdateService.cs` | 检查、下载、校验、应用补丁更新和工具更新 |
| `src/UI/*` | 界面，自绘控件 |
| `build.ps1` / `release.ps1` | 构建 / 签名并发布 |

注意：官方原版的脚本打包在 `game/scripts.rpa` 里，没有单独的 `options.rpy`；`game/script_version.txt` 记录的是 Ren'Py 引擎版本 `(8, 5, 3)`，不是游戏版本。

### 命令行（提权和测试用）

```
LocaForge.exe --scan
LocaForge.exe --status    "游戏目录"
LocaForge.exe --install   "游戏目录"
LocaForge.exe --uninstall "游戏目录"
LocaForge.exe --check-update                 # 只检查
LocaForge.exe --update                       # 下载并应用补丁更新和工具更新（不重启）
LocaForge.exe --render out.png ["游戏目录"]   # 把界面画成图片
```

测试时可用环境变量 `LOCAFORGE_UPDATE_BASE` 把更新源指向本地服务器，`LOCAFORGE_CACHE_DIR` 换一个缓存目录。

### 加新游戏或新版本

在 `catalog.json` 里加一个 `games` 项或一个 `patches` 项，补丁 zip 放进 `publish\payload\`。
新的官方游戏版本要在 `versionFingerprints` 里加一条指纹（`size` / `sha256`），否则会被当成"未收录的版本"。

- `exeNames` 可以写通配符（如 `Realm_of_Evergrow*.exe`），玩家给单文件游戏改名也能认出
- `exeCheck: "godot"` 按文件内容确认是 Godot 游戏（exe 末尾有 `GDPC` 资源包标记，或旁边有同名 `.pck`），排除同名的安装包；7-Zip、WinRAR 等自解压安装包对所有游戏都会被排除
- 指纹的 `path` 写 `{exe}` 表示直接对游戏程序本身做指纹，适合资源内嵌在 exe 里的游戏
- `saveDirectory` 写存档目录（可用 `%APPDATA%`），不写时按 Ren'Py 规则推算

### Godot 单文件游戏（资源内嵌在 exe 里）

这类游戏不会读取 exe 旁边的 `.pck`，所以用"外挂补丁"：

```powershell
python tools\godot\build_godot_patch.py --original 原版.exe --translated 完整汉化.pck --out publish\payload\xxx.zip [--autoload ZhUI=res://zh/zh_ui.gd]
```

脚本只提取汉化改动和新增的文件，打成 `zh\zh_patch.pck`；再读取原版的 `project.binary`，用 `override.cfg` 接管游戏的**第一个自动加载**：
它改为加载 `zh\<名字>.tscn`，这个场景先引用 `zh\zh_hook.gd`（加载时立即挂上补丁包），再引用游戏原本的脚本。
这样补丁在游戏任何脚本、场景、剧情加载之前生效，节点名和单例名不变。脚本最后会输出 exe 的版本指纹，填进 `catalog.json`。

做法的验证过程：补丁挂得太晚（作为最后一个自动加载）时，已有 58 个改动资源被游戏提前加载成英文；挂在加载翻译文件的阶段又太早，脚本系统未就绪会崩溃。
首个自动加载是脚本时，生成的场景根节点默认是 `Node`，如果它继承的是别的节点类型，用 `--root-type` 指定。
