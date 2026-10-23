"""Archive the sources actually used by the Windows Pandoc build."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct
import tarfile
import time
from urllib.request import urlopen
from zipfile import ZipFile, ZIP_DEFLATED, ZIP_STORED
import os


def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def write_json(path, data):
    Path(path).write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def load(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def prepare(work, cabal_root):
    materials = work / 'materials'
    source = materials / 'source'
    plan = load(materials / 'provenance/resolved-plan.json')
    packages = {}
    for package in plan['install-plan']:
        origin = package.get('pkg-src', {})
        if origin.get('type') == 'local' or package['type'] == 'pre-existing':
            continue
        if origin.get('type') != 'repo-tar':
            raise ValueError('Unexpected source origin: ' + repr(origin))
        identity = package['pkg-name'] + '-' + package['pkg-version']
        item = {'name': package['pkg-name'], 'version': package['pkg-version'],
                'tarSha256': package.get('pkg-src-sha256'),
                'cabalSha256': package.get('pkg-cabal-sha256'), 'flags': package.get('flags', {})}
        for key in ('tarSha256', 'cabalSha256'):
            if not re.fullmatch('[a-f0-9]{64}', item[key] or ''):
                raise ValueError('Missing secure-index source hash: ' + identity + ' ' + key)
        if identity in packages and packages[identity] != item:
            raise ValueError('Conflicting dependency variants: ' + identity)
        packages[identity] = item
    if not packages:
        raise ValueError('No dependency source packages resolved')
    print(f'Resolved {len(packages)} dependency source archives', flush=True)
    vendor = source / 'vendor'
    archives = materials / 'dependency-archives'
    vendor.mkdir()
    archives.mkdir()

    def download(entry):
        identity, item = entry
        destination = archives / (identity + '.tar.gz')
        url = f'https://hackage.haskell.org/package/{identity}/{identity}.tar.gz'
        for attempt in range(4):
            try:
                with urlopen(url, timeout=90) as response, destination.open('wb') as stream:
                    shutil.copyfileobj(response, stream)
                if digest(destination) != item['tarSha256']:
                    raise ValueError('Dependency archive checksum mismatch: ' + identity)
                break
            except OSError:
                if attempt == 3:
                    raise
                time.sleep(attempt + 1)
        with tarfile.open(destination) as archive:
            archive.extractall(vendor, filter='data')
        item['url'] = url
        return identity

    with ThreadPoolExecutor(max_workers=6) as pool:
        for identity in pool.map(download, sorted(packages.items())):
            print('Archived source: ' + identity, flush=True)

    # The secure index can contain multiple revisions of a Cabal file. Select
    # the exact body named by the resolved plan rather than the latest revision.
    wanted = {f"{p['name']}/{p['version']}/{p['name']}.cabal": (identity, p)
              for identity, p in packages.items()}
    indexes = list(cabal_root.rglob('01-index.tar'))
    if len(indexes) != 1:
        raise ValueError('Expected one secure Hackage index: ' + repr(indexes))
    with tarfile.open(indexes[0], mode='r|') as index:
        for entry in index:
            if entry.name not in wanted or not entry.isfile():
                continue
            identity, item = wanted[entry.name]
            body = index.extractfile(entry).read()
            if hashlib.sha256(body).hexdigest() != item['cabalSha256']:
                continue
            (vendor / identity / (item['name'] + '.cabal')).write_bytes(body)
            (archives / (identity + '.cabal')).write_bytes(body)
            del wanted[entry.name]
            if not wanted:
                break
    if wanted:
        raise ValueError('Exact Cabal revisions missing from secure index: ' + repr(list(wanted)))

    locations = ['pandoc-3.10', 'pandoc-3.10/pandoc-cli', 'pandoc-3.10/pandoc-lua-engine', 'pandoc-3.10/pandoc-server', 'vendor/*']
    lines = ['packages: ' + '\n          '.join(locations), 'active-repositories: :none',
             'optimization: 1', 'tests: False', 'benchmarks: False', '']
    flags = {}
    for package in plan['install-plan']:
        if package['type'] != 'configured':
            continue
        name = package['pkg-name']
        current = package.get('flags', {})
        if name in flags and flags[name] != current:
            raise ValueError('Conflicting package flags: ' + name)
        flags[name] = current
    for name, values in sorted(flags.items()):
        if values:
            lines += ['package ' + name, '  flags: ' + ' '.join(('+' if value else '-') + flag for flag, value in sorted(values.items())), '']
    (source / 'cabal.project').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    write_json(materials / 'provenance/dependency-sources.json', packages)


def imports(path):
    data = path.read_bytes()
    pe = struct.unpack_from('<I', data, 0x3c)[0]
    if data[pe:pe+4] != b'PE\0\0':
        raise ValueError('Expected Windows PE executable')
    count = struct.unpack_from('<H', data, pe+6)[0]
    optional_size = struct.unpack_from('<H', data, pe+20)[0]
    optional = pe + 24
    if struct.unpack_from('<H', data, optional)[0] != 0x20b:
        raise ValueError('Expected x64 PE32+ executable')
    section_start = optional + optional_size
    sections = []
    for i in range(count):
        size, address, raw_size, raw = struct.unpack_from('<IIII', data, section_start + i*40 + 8)
        sections.append((address, max(size, raw_size), raw))

    def offset(rva):
        for address, size, raw in sections:
            if address <= rva < address + size:
                return raw + rva - address
        raise ValueError('PE RVA outside sections')

    result = []
    import_rva = struct.unpack_from('<I', data, optional+112+8)[0]
    if struct.unpack_from('<I', data, optional+112+13*8)[0]:
        raise ValueError('Delay-loaded DLL imports require separate review')
    if import_rva:
        cursor = offset(import_rva)
        while any(data[cursor:cursor+20]):
            name_rva = struct.unpack_from('<I', data, cursor+12)[0]
            start = offset(name_rva)
            result.append(data[start:data.index(b'\0', start)].decode('ascii'))
            cursor += 20
    return result


def package(work):
    materials = work / 'materials'
    source = materials / 'source'
    provenance = materials / 'provenance'
    plan = load(provenance / 'built-plan.json')
    globals_text = (provenance / 'ghc-global-packages.txt').read_text(encoding='utf-8-sig')
    global_ids = set(re.findall(r'^id:\s*(\S+)', globals_text, re.M))
    dependencies = load(provenance / 'dependency-sources.json')
    preexisting = []
    built_packages = []
    for item in plan['install-plan']:
        if item['type'] == 'pre-existing':
            if item['id'] not in global_ids:
                raise ValueError('Unarchived pre-existing package: ' + item['id'])
            preexisting.append(item['id'])
            continue
        origin = item.get('pkg-src', {})
        origin_path = Path(origin.get('path', ''))
        if not origin_path.is_absolute():
            origin_path = source / origin_path
        if origin.get('type') != 'local' or not origin_path.resolve().is_relative_to(source.resolve()):
            raise ValueError('Build used a non-local source: ' + repr(origin))
        built_packages.append(item['pkg-name'] + '-' + item['pkg-version'])
    if not built_packages or not preexisting:
        raise ValueError('Unexpected empty build plan')
    binary = work / 'binary/pandoc-3.10'
    executable = binary / 'pandoc.exe'
    dlls = imports(executable)
    system_dlls = {'kernel32.dll', 'msvcrt.dll', 'ws2_32.dll', 'advapi32.dll', 'shell32.dll', 'user32.dll', 'bcrypt.dll', 'ole32.dll', 'shlwapi.dll', 'ntdll.dll', 'ucrtbase.dll', 'iphlpapi.dll', 'secur32.dll', 'crypt32.dll', 'version.dll', 'winmm.dll', 'comdlg32.dll', 'gdi32.dll', 'comctl32.dll', 'dbghelp.dll', 'rpcrt4.dll'}
    unexpected = [name for name in dlls if name.lower() not in system_dlls and not name.lower().startswith(('api-ms-win-', 'ext-ms-win-'))]
    if unexpected:
        raise ValueError('Additional runtime DLLs require review: ' + repr(unexpected))
    licenses = binary / 'licenses'
    for identity, item in dependencies.items():
        folder = source / 'vendor' / identity
        notices = [p for p in folder.rglob('*') if p.is_file() and re.match(r'^(license|licence|copying|copyright|notice)([.\-_]|$)', p.name, re.I)]
        if not notices:
            raise ValueError('Dependency license text missing: ' + identity)
        for path in notices + [folder / (item['name'] + '.cabal')]:
            target = licenses / identity / path.relative_to(folder)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, target)
    with tarfile.open(materials / 'toolchain-source/ghc-9.10.3-src.tar.xz') as compiler:
        for entry in compiler:
            if entry.isfile() and re.match(r'^(LICENSE|COPYING)(\.|$)', Path(entry.name).name, re.I):
                target = licenses / 'ghc-9.10.3' / entry.name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(compiler.extractfile(entry).read())
    config = load(provenance / 'pandoc-build.json')
    version = (provenance / 'pandoc-version.txt').read_text(encoding='utf-8-sig')
    if not version.startswith('pandoc 3.10\n'):
        raise ValueError('Unexpected built Pandoc version')
    readme = '''# Pandoc3.10对应源码与构建材料

本程序由md2word项目自行构建，不冒称Pandoc官方发布二进制。source包含实际参与构建的Pandoc与依赖源码，依赖Cabal修订按安全索引中的摘要逐项匹配。toolchain-source另附GHC及Windows配套源码，provenance保留解析计划、实际构建计划、编译器包记录和构建脚本。

准备GHC9.10.3、Cabal3.14.2.0及Windows x64编译环境后，在source目录执行：

    cabal build pandoc-cli:exe:pandoc --offline --project-file=cabal.project -j2

项目关闭远程依赖仓库，依赖均为归档中的本地源码。初次准备编译工具仍需下载或自行提供编译器；不承诺产物逐字节可复现。该源码材料不表示md2word已通过所有系统与Word版本验收。
'''
    (materials / 'README.md').write_text(readme, encoding='utf-8')
    record = {'provider': 'md2word-source-build', 'version': '3.10', 'sourceCommit': config['sourceCommit'], 'ghcVersion': config['ghcVersion'], 'cabalVersion': config['cabalVersion'], 'indexState': config['indexState'], 'exeSha256': digest(executable), 'configuredPackages': sorted(set(built_packages)), 'compilerPackages': sorted(preexisting), 'dllImports': dlls, 'workflow': 'https://github.com/' + os.environ.get('GITHUB_REPOSITORY', '') + '/actions/runs/' + os.environ.get('GITHUB_RUN_ID', ''), 'builderCommit': os.environ.get('GITHUB_SHA'), 'remoteRepositoriesDisabled': True, 'correspondingSourceComplete': True}
    write_json(provenance / 'BUILD.json', record)
    files = sorted(p for p in materials.rglob('*') if p.is_file())
    write_json(materials / 'FILES.json', [{'path': p.relative_to(materials).as_posix(), 'sha256': digest(p)} for p in files])
    output = work / 'output'
    output.mkdir()
    source_zip = output / 'pandoc-3.10-corresponding-sources.zip'
    with ZipFile(source_zip, 'w', compression=ZIP_DEFLATED, compresslevel=6) as archive:
        for path in sorted(p for p in materials.rglob('*') if p.is_file()):
            archive.write(path, path.relative_to(materials).as_posix(), compress_type=ZIP_STORED if path.suffix == '.xz' else ZIP_DEFLATED)
    record['sourceArchiveSha256'] = digest(source_zip)
    write_json(binary / 'SOURCE.json', record)
    binary_zip = output / 'pandoc-3.10-md2word-win-x64.zip'
    with ZipFile(binary_zip, 'w', compression=ZIP_DEFLATED, compresslevel=6) as archive:
        for path in sorted(p for p in binary.rglob('*') if p.is_file()):
            archive.write(path, path.relative_to(binary.parent).as_posix())
    record['binaryArchiveSha256'] = digest(binary_zip)
    write_json(output / 'BUNDLE.json', record)
    (output / 'SHA256SUMS.txt').write_text(''.join(digest(p) + '  ' + p.name + '\n' for p in (binary_zip, source_zip, output / 'BUNDLE.json')), encoding='utf-8')
    print(json.dumps(record, indent=2), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('phase', choices=['prepare', 'package'])
    parser.add_argument('--work', type=Path, required=True)
    parser.add_argument('--cabal-root', type=Path)
    arguments = parser.parse_args()
    if arguments.phase == 'prepare':
        prepare(arguments.work.resolve(), arguments.cabal_root.resolve())
    else:
        package(arguments.work.resolve())
