#!/usr/bin/env python3
"""Recreate MIT-licensed synthetic video test inputs; requires a full system FFmpeg."""
import subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[1]/'tests/fixtures/generated'
def run(*args):subprocess.run(['ffmpeg','-v','error',*args],check=True)
run('-f','lavfi','-i','testsrc2=size=160x90:rate=25','-f','lavfi','-i','sine=frequency=440:sample_rate=48000',
    '-t','2','-c:v','mpeg2video','-c:a','mp2','-y',str(root/'movie.mpg'))
run('-f','lavfi','-i','testsrc2=size=160x90:rate=24','-t','1','-c:v','mpeg4','-an','-y',str(root/'silent.mp4'))
