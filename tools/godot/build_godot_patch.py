"""
把 Godot 4 单文件游戏（资源包内嵌在 exe 里）的汉化做成"外挂补丁"，供 LocaForge 安装。

    python tools/godot/build_godot_patch.py --original 原版.exe --translated 完整汉化.pck --out 补丁.zip
                                            [--autoload ZhUI=res://zh/zh_ui.gd] [--root-type Node] [--locale zh]

补丁 zip 解压到游戏目录即生效，exe 一个字节都不改：

    override.cfg            Godot 会读取 exe 旁边的这个文件
    zh/zh_patch.pck         只含汉化改动/新增的文件
    zh/zh_hook.gd           脚本一加载就挂上 zh_patch.pck，并注册 zh/ 下的翻译
    zh/<首个自动加载>.tscn   接管游戏的第一个自动加载：先引用 zh_hook.gd，再引用游戏原本的脚本/场景

为什么要接管第一个自动加载：游戏的自动加载会预加载大量脚本和场景，补丁必须在它们之前挂上。
Godot 加载翻译文件的阶段又太早（脚本系统未就绪会崩溃，文本资源也不能读），
所以选择"第一个自动加载被创建的那一刻"。节点名、单例名不变，游戏代码察觉不到。

只读原版和汉化包，不修改任何输入文件。
"""
import argparse, hashlib, os, struct, sys, zipfile

PACK_REL_FILEBASE = 2


# ---------------- PCK 读取 ----------------
def read_index(path):
    """返回 (起点偏移, {文件名: (绝对偏移, 大小, md5, flags)}, 引擎版本)。自动识别是否内嵌在 exe 末尾。"""
    with open(path, 'rb') as f:
        f.seek(-12, 2)
        size, magic = struct.unpack('<Q4s', f.read(12))
        start = f.tell() - 12 - size if magic == b'GDPC' else 0
        f.seek(start)
        if f.read(4) != b'GDPC':
            raise SystemExit(f'不是 Godot 资源包，也没有内嵌资源包：{path}')
        ver, major, minor, patch, flags, files_base = struct.unpack('<IIIIIQ', f.read(28))
        if ver != 2:
            raise SystemExit(f'暂只支持 PCK v2（Godot 4.0–4.4），这个是 v{ver}：{path}')
        if flags & 1:
            raise SystemExit(f'资源包目录已加密，无法处理：{path}')
        f.read(64)
        if flags & PACK_REL_FILEBASE:
            files_base += start
        n, = struct.unpack('<I', f.read(4))
        idx = {}
        for _ in range(n):
            ln, = struct.unpack('<I', f.read(4))
            name = f.read(ln).rstrip(b'\0').decode('utf-8')
            ofs, sz = struct.unpack('<QQ', f.read(16))
            md5 = f.read(16)
            ff, = struct.unpack('<I', f.read(4))
            idx[name] = (ofs + files_base, sz, md5, ff)
        return start, idx, f'{major}.{minor}.{patch}'


def read_file(path, entry):
    with open(path, 'rb') as f:
        f.seek(entry[0])
        return f.read(entry[1])


def write_pck(files, engine):
    """Godot 4 PCK v2，文件按 32 字节对齐，使用相对文件基址。"""
    major, minor, patch = (int(x) for x in engine.split('.'))
    names = [(k, k.encode() + b'\0' * ((-len(k.encode())) % 4), v) for k, v in sorted(files.items())]
    base = 4 + 28 + 64 + 4 + sum(4 + len(n) + 16 + 16 + 4 for _, n, _ in names)
    base = (base + 31) // 32 * 32
    head = bytearray(b'GDPC' + struct.pack('<IIIIIQ', 2, major, minor, patch, PACK_REL_FILEBASE, base) + bytes(64) + struct.pack('<I', len(names)))
    data = bytearray()
    for _, n, v in names:
        head += struct.pack('<I', len(n)) + n + struct.pack('<QQ', len(data), len(v)) + hashlib.md5(v).digest() + struct.pack('<I', 0)
        data += v
        data += bytes((-len(data)) % 32)
    return bytes(head) + bytes(base - len(head)) + bytes(data)


# ---------------- project.binary ----------------
def autoloads(project_binary):
    """按顺序返回 [(名字, 路径, 是否单例)]。project.binary 格式：ECFG + 条目数 + (键, Variant) 列表。"""
    b = project_binary
    if b[:4] != b'ECFG':
        raise SystemExit('project.binary 格式不对')
    n, = struct.unpack_from('<I', b, 4)
    pos, out = 8, []
    for _ in range(n):
        kl, = struct.unpack_from('<I', b, pos); pos += 4
        key = b[pos:pos + kl].decode('utf-8'); pos += kl
        vl, = struct.unpack_from('<I', b, pos); pos += 4
        val = b[pos:pos + vl]; pos += vl
        if not key.startswith('autoload/'):
            continue
        vtype, = struct.unpack_from('<I', val, 0)
        if vtype & 0xFFFF != 4:      # Variant::STRING
            continue
        sl, = struct.unpack_from('<I', val, 4)
        s = val[8:8 + sl].decode('utf-8')
        out.append((key[len('autoload/'):], s.lstrip('*'), s.startswith('*')))
    return out


# ---------------- 生成的文件 ----------------
HOOK = '''extends RefCounted
## LocaForge 外挂汉化补丁的启动钩子（自动生成，勿手改）。
## 由 zh/{first}.tscn 第一个引用；{first} 是游戏的第一个自动加载，
## 所以补丁包在游戏任何脚本、场景、剧情被加载之前就已挂上。

static func _static_init() -> void:
\tvar dir := OS.get_executable_path().get_base_dir()
\tif not ProjectSettings.load_resource_pack(dir.path_join("zh/zh_patch.pck"), true):
\t\tpush_error("[LocaForge] 无法挂载 zh/zh_patch.pck，游戏将以原版运行")
\t\treturn
\tfor f in DirAccess.get_files_at("res://zh"):
\t\tif f.ends_with(".translation"):
\t\t\tTranslationServer.add_translation(load("res://zh/" + f))
'''

SCENE_SCRIPT = '''[gd_scene load_steps=3 format=3]

[ext_resource type="Script" path="res://zh/zh_hook.gd" id="1_hook"]
[ext_resource type="Script" path="{path}" id="2_orig"]

[node name="{first}" type="{root_type}"]
script = ExtResource("2_orig")
metadata/locaforge_hook = ExtResource("1_hook")
'''

SCENE_INSTANCE = '''[gd_scene load_steps=3 format=3]

[ext_resource type="Script" path="res://zh/zh_hook.gd" id="1_hook"]
[ext_resource type="PackedScene" path="{path}" id="2_orig"]

[node name="{first}" instance=ExtResource("2_orig")]
metadata/locaforge_hook = ExtResource("1_hook")
'''


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--original', required=True, help='原版游戏 exe（资源包内嵌）或原版 .pck')
    ap.add_argument('--translated', required=True, help='完整的汉化资源包 .pck')
    ap.add_argument('--out', required=True, help='输出的补丁 zip')
    ap.add_argument('--autoload', action='append', default=[], help='额外的自动加载，如 ZhUI=res://zh/zh_ui.gd（必须在汉化包里）')
    ap.add_argument('--root-type', default='Node', help='首个自动加载是脚本时，它继承的节点类型（默认 Node）')
    ap.add_argument('--locale', default='zh', help='强制使用的语言；传空字符串则跟随系统')
    a = ap.parse_args()

    o_start, orig, engine = read_index(a.original)
    _, zh, zh_engine = read_index(a.translated)
    if engine.split('.')[:2] != zh_engine.split('.')[:2]:
        raise SystemExit(f'引擎版本不一致：原版 {engine}，汉化包 {zh_engine}')

    changed = [n for n, e in zh.items() if n in orig and orig[n][2] != e[2]]
    added = [n for n in zh if n not in orig]
    removed = [n for n in orig if n not in zh]
    if removed:
        print(f'注意：汉化包比原版少了 {len(removed)} 个文件，外挂补丁无法删除原版文件，这些文件会保持原样', file=sys.stderr)
    for n in changed + added:
        if zh[n][3] & 1:
            raise SystemExit(f'汉化包里的 {n} 是加密的，无法处理')
    files = {n: read_file(a.translated, zh[n]) for n in changed + added}
    if 'project.binary' in files:
        raise SystemExit('汉化包改动了 project.binary：外挂补丁无法替换项目设置，请改用 override.cfg 实现这些设置')

    loads = autoloads(read_file(a.original, orig['project.binary']))
    if not loads:
        raise SystemExit('原版没有自动加载，无法接管启动时机')
    first, first_path, singleton = loads[0]
    if first_path.endswith(('.gd', '.gdc')):
        scene = SCENE_SCRIPT.format(first=first, path=first_path.replace('.gdc', '.gd'), root_type=a.root_type)
    elif first_path.endswith(('.tscn', '.scn')):
        scene = SCENE_INSTANCE.format(first=first, path=first_path)
    else:
        raise SystemExit(f'首个自动加载类型不支持：{first_path}')

    extra = []
    for spec in a.autoload:
        name, _, path = spec.partition('=')
        rel = path.replace('res://', '')
        if rel not in files and rel not in orig:
            raise SystemExit(f'额外自动加载 {path} 不在汉化包里')
        extra.append(f'{name}="*{path}"')

    cfg = ['; LocaForge 外挂汉化补丁（自动生成）。游戏 exe 不做任何修改；删除本文件和 zh 文件夹即恢复原版。',
           f'; {first} 是游戏的第一个自动加载，这里改为 zh/{first}.tscn：先挂上 zh/zh_patch.pck，再加载游戏原本的 {first_path}。',
           '']
    if a.locale:
        cfg += ['[internationalization]', f'locale/test="{a.locale}"', '']
    cfg += ['[autoload]', f'{first}="{"*" if singleton else ""}res://zh/{first}.tscn"'] + extra + ['']

    pck = write_pck(files, engine)
    out = {
        'override.cfg': '\n'.join(cfg).encode('utf-8'),
        'zh/zh_hook.gd': HOOK.format(first=first).encode('utf-8'),
        f'zh/{first}.tscn': scene.encode('utf-8'),
        'zh/zh_patch.pck': pck,
    }
    with zipfile.ZipFile(a.out, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for name in sorted(out):
            info = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.flag_bits |= 0x800          # 文件名用 UTF-8
            z.writestr(info, out[name])

    h = hashlib.sha256()
    with open(a.original, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    size = os.path.getsize(a.original)
    print(f'引擎 Godot {engine}，原版 {len(orig)} 个文件；补丁含改动 {len(changed)} 个、新增 {len(added)} 个')
    print(f'接管首个自动加载：{first} -> {first_path}')
    print(f'zh_patch.pck {len(pck) / 1048576:.1f} MB，补丁 zip {os.path.getsize(a.out) / 1048576:.1f} MB')
    print('catalog 版本指纹：{"path": "{exe}", "size": %d, "sha256": "%s"}' % (size, h.hexdigest()))


if __name__ == '__main__':
    main()
