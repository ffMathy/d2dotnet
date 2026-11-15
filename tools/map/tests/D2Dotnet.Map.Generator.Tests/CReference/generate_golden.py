#!/usr/bin/env python3
"""
Builds the *upstream* C map generator (blacha/diablo2 packages/map/map) for 32-bit Linux
against the fake Diablo 2 in fake_d2.cpp, runs every scenario in scenarios.json and writes
the exact stdout bytes and exit code to ../Golden/<name>.out / <name>.code.

Usage: python3 -I generate_golden.py <upstream packages/map/map directory>
Needs g++ with -m32 support (Debian/Ubuntu: g++-multilib).

The upstream sources are copied unmodified except that the two inline-asm "magic jump"
bodies in d2_client.c are removed (they jump to a hard-coded address inside D2Client.dll).
offset.c is replaced by fake_d2.cpp's DefineOffsets, and gettimeofday is pinned to 1000 ms.
"""
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

here = os.path.dirname(os.path.abspath(__file__))
upstream = os.path.abspath(sys.argv[1])
golden = os.path.join(here, '..', 'Golden')
os.makedirs(golden, exist_ok=True)

work = tempfile.mkdtemp(prefix='d2map-cref-')
src = os.path.join(work, 'map')
shutil.copytree(upstream, src)

client = os.path.join(src, 'd2_client.c')
code = open(client).read()
# Keep the line count identical so __LINE__ in later log calls is unchanged.
code, n = re.subn(r'__asm\((?:[^;]|\n)*?\);', lambda m: '/* magic jump removed */' + '\n' * m.group(0).count('\n'), code)
assert n == 2, n
open(client, 'w').write(code)

exe = os.path.join(work, 'd2-map-ref')
subprocess.check_call(['gcc', '-m32', '-c', os.path.join(here, 'fake_time.c'), '-o', os.path.join(work, 'fake_time.o')])
cmd = ['g++', '-m32', '-no-pie', '-w', '-fpermissive', '-std=gnu++14', '-include', 'string.h',
       '-I', os.path.join(here, 'stub'), '-I', src,
       '-DGIT_VERSION="v-test"', '-DGIT_HASH="abc123"', '-Dgettimeofday=fake_gettimeofday',
       '-o', exe]
# Relative paths from the build root, like build.mapgen.sh, so __FILE__ is "map/main.c" etc.
cmd += ['map/' + f for f in ['json.c', 'log.c', 'map.c', 'd2_client.c', 'main.c']]
cmd += [os.path.join(here, 'fake_d2.cpp'), os.path.join(work, 'fake_time.o')]
subprocess.check_call(cmd, cwd=work)

for sc in json.load(open(os.path.join(here, 'scenarios.json'))):
    run = os.path.join(work, 'run-' + sc['name'])
    os.makedirs(run)
    for f in sc['files']:
        path = os.path.join(run, f)  # backslashes are literal file-name characters on Linux
        os.makedirs(os.path.dirname(path), exist_ok=True)
        open(path, 'w').close()
    env = dict(os.environ)
    if sc.get('noRegistry'):
        env['FAKE_NO_REGISTRY'] = '1'
    p = subprocess.run([exe] + sc['args'], input=sc['stdin'].encode('latin-1'), cwd=run, env=env,
                       stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    open(os.path.join(golden, sc['name'] + '.out'), 'wb').write(p.stdout)
    open(os.path.join(golden, sc['name'] + '.code'), 'w').write(str(p.returncode) + '\n')
    print(sc['name'], p.returncode, len(p.stdout), 'bytes', ('stderr: ' + p.stderr.decode()) if p.stderr else '')

shutil.rmtree(work)
