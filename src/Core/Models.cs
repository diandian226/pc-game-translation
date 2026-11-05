using System.Collections.Generic;
using System.Runtime.Serialization;

namespace FMGPatcher.Core
{
    /// <summary>catalog.json：工具支持哪些游戏、每个版本对应哪个补丁包。与安卓版 VersionCatalog 同一思路。</summary>
    [DataContract]
    public sealed class Catalog
    {
        [DataMember(Name = "games")] public List<GameProfile> Games = new List<GameProfile>();
    }

    [DataContract]
    public sealed class GameProfile
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "name")] public string Name;
        /// <summary>游戏启动程序文件名，用于识别目录和检测是否正在运行。</summary>
        [DataMember(Name = "exeNames")] public List<string> ExeNames = new List<string>();
        /// <summary>目录里必须存在的文件或文件夹（相对路径），用来确认"这真的是这个游戏"。</summary>
        [DataMember(Name = "markers")] public List<string> Markers = new List<string>();
        [DataMember(Name = "versionFile")] public string VersionFile;
        [DataMember(Name = "versionRegex")] public string VersionRegex;
        /// <summary>官方各版本关键文件（如 game/scripts.rpa）的大小和哈希，用来识别版本、排除修改版。</summary>
        [DataMember(Name = "versionFingerprints")] public List<FileHash> VersionFingerprints = new List<FileHash>();
        /// <summary>手动复制过补丁的特征文件：没有安装记录但这些文件存在，就判定为"已手动汉化"。</summary>
        [DataMember(Name = "patchedMarkers")] public List<string> PatchedMarkers = new List<string>();
        [DataMember(Name = "steamAppId")] public string SteamAppId;
        /// <summary>扫描磁盘时优先进入的文件夹名。</summary>
        [DataMember(Name = "searchFolderNames")] public List<string> SearchFolderNames = new List<string>();
        [DataMember(Name = "patches")] public List<PatchPackage> Patches = new List<PatchPackage>();

        public override string ToString() => Name;
    }

    [DataContract]
    public sealed class PatchPackage
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "title")] public string Title;
        [DataMember(Name = "gameVersion")] public string GameVersion;
        [DataMember(Name = "patchVersion")] public string PatchVersion;
        /// <summary>补丁 zip，相对工具 exe 所在目录。zip 内部路径即游戏根目录下的相对路径（如 game/script.rpy）。</summary>
        [DataMember(Name = "payload")] public string Payload;
        /// <summary>zip 内若有一层多余的根目录，在这里写上，安装时会去掉。</summary>
        [DataMember(Name = "payloadRoot")] public string PayloadRoot;
        /// <summary>覆盖 .rpy 时把同名旧 .rpyc 移到备份，让 Ren'Py 重新编译。</summary>
        [DataMember(Name = "removeStaleCompiled")] public bool RemoveStaleCompiled;
        /// <summary>可选：原版关键文件的哈希，用于精确校验版本。</summary>
        [DataMember(Name = "requiredFiles")] public List<FileHash> RequiredFiles = new List<FileHash>();
    }

    [DataContract]
    public sealed class FileHash
    {
        [DataMember(Name = "version")] public string Version;
        [DataMember(Name = "path")] public string Path;
        [DataMember(Name = "size")] public long Size;
        [DataMember(Name = "sha256")] public string Sha256;
    }

    /// <summary>写在 &lt;游戏目录&gt;\_zh_patch\install.json，卸载/还原全靠它。</summary>
    [DataContract]
    public sealed class InstallRecord
    {
        [DataMember(Name = "state")] public string State;          // installing | installed
        [DataMember(Name = "profileId")] public string ProfileId;
        [DataMember(Name = "patchId")] public string PatchId;
        [DataMember(Name = "patchVersion")] public string PatchVersion;
        [DataMember(Name = "gameVersion")] public string GameVersion;
        [DataMember(Name = "toolVersion")] public string ToolVersion;
        [DataMember(Name = "installedAt")] public string InstalledAt;
        [DataMember(Name = "files")] public List<InstalledFile> Files = new List<InstalledFile>();
        /// <summary>被移进备份的旧编译文件（.rpyc），还原时放回。</summary>
        [DataMember(Name = "removed")] public List<string> Removed = new List<string>();
        /// <summary>补丁新建的文件夹，还原时若为空则删除。</summary>
        [DataMember(Name = "createdDirs")] public List<string> CreatedDirs = new List<string>();
    }

    [DataContract]
    public sealed class InstalledFile
    {
        [DataMember(Name = "path")] public string Path;
        [DataMember(Name = "sha256")] public string Sha256;
        /// <summary>true = 覆盖了原文件，原文件在备份里；false = 补丁新增的文件。</summary>
        [DataMember(Name = "backedUp")] public bool BackedUp;
    }
}
