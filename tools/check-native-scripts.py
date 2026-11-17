#!/usr/bin/env python3
"""Focused public CLI and offline-package checks for managed script imports."""
import importlib.util
import json
import shutil
import subprocess
import tempfile
import tarfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CLI = ROOT / 'modern/GARbro.Cli/bin/Release/net11.0/linux-x64/garbro.dll'
FIXTURES = ROOT / 'tests/fixtures/scripts-native'


def run(*arguments):
    result = subprocess.run(['dotnet', str(CLI), '--json', *map(str, arguments)],
                            capture_output=True, text=True, cwd=ROOT, timeout=60)
    if result.returncode:
        raise AssertionError((arguments, result.returncode, result.stdout, result.stderr))
    return json.loads(result.stdout)


def main():
    with tempfile.TemporaryDirectory(prefix='garbro-中文-日本語-') as temporary:
        root = Path(temporary)
        source, output, rebuilt = (root / name for name in ('input files', 'output', 'rebuilt'))
        source.mkdir()
        original = source / 'Scripts.rvdata2'
        shutil.copy2(FIXTURES / 'scripts.rvdata2', original)
        template = output / 'Scripts.rvdata2.rgss.json'
        assert run('convert', original, '-o', template)['format'] == 'RGSS-JSON'
        data = json.loads(template.read_text())
        data['scripts'][0]['source'] = "puts '変更後'\n"
        template.write_text(json.dumps(data, ensure_ascii=False), encoding='utf-8')
        restored = rebuilt / 'Scripts.rvdata2'
        assert run('convert', template, '-o', restored)['format'] == 'RGSS'
        ruby = output / 'scripts.rb'
        run('convert', restored, '-o', ruby, '--to', 'RB')
        assert '変更後' in ruby.read_text()
        assert original.read_bytes() == (FIXTURES / 'scripts.rvdata2').read_bytes()

        archive = source / 'SCRIPT.PAK'
        shutil.copy2(FIXTURES / 'luca-LOOPERS.pak', archive)
        template = output / 'SCRIPT.PAK.luca.json'
        run('convert', archive, '-o', template, '--scheme', 'LOOPERS')
        restored = rebuilt / 'SCRIPT.PAK'
        run('convert', template, '-o', restored)
        assert restored.read_bytes() == archive.read_bytes()

        run('convert', FIXTURES / 'renpy-rpc2.rpyc', '-o', output / 'native.rpy')
        assert 'call second from native_return' in (output / 'native.rpy').read_text()
        compiled = source / 'script.tjs'
        shutil.copy2(FIXTURES / 'tjs-imt-initialize.tjsbc', compiled)
        run('convert', compiled, '-o', output / 'readable.tjs')
        assert 'class PoolWindow' in (output / 'readable.tjs').read_text()

    spec = importlib.util.spec_from_file_location('prepare_flathub', ROOT / 'tools/prepare-flathub.py')
    offline = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(offline)
    assets = json.loads((ROOT / 'modern/GARbro.Cli/obj/project.assets.json').read_text())
    assert 'superpower/3.1.0' in dict(offline.package_records(assets))
    assert (CLI.parent / 'Furikiri.Core.dll').is_file()
    spec = importlib.util.spec_from_file_location('package_release', ROOT / 'tools/package-release.py')
    packaging = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(packaging)
    with tempfile.TemporaryDirectory() as temporary:
        archive = Path(temporary) / 'Furikiri-source.tar.gz'
        packaging.script_source_archive(archive)
        with tarfile.open(archive) as source:
            assert 'third_party/Furikiri/Furikiri.Core/Emit/Module.cs' in source.getnames()
            assert 'modern/GARbro.Scripting.Tjs/GARbro.Scripting.Tjs.csproj' in source.getnames()
        source_size = archive.stat().st_size
    desktop = ROOT / 'modern/GARbro.Desktop/bin/Release/net11.0/linux-x64'
    sizes = {name: (desktop / name).stat().st_size for name in
             ('GARbro.Scripting.dll', 'Furikiri.Core.dll', 'Superpower.dll',
              'GARbro.Application.dll', 'GARbro.UI.dll', 'GARbro.Domain.dll',
              'ArcFormats.Portable.dll', 'GameData/Formats.json.gz')}
    report = {'cliConversions': 7, 'offlineSuperpowerDiscovered': True,
              'helperRuntimesRequired': False, 'linuxX64ComponentBytes': sizes,
              'correspondingSourceArchiveBytes': source_size}
    target = ROOT / 'artifacts/native-script-check.json'
    target.parent.mkdir(exist_ok=True)
    target.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
