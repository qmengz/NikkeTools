import os
import subprocess
from pathlib import Path
from concurrent.futures import ProcessPoolExecutor

thispath = Path(__file__).resolve().parent
outDir = thispath / 'Decompiled'
inDir = thispath / 'Lua'

def process_file(args):
    fdir, file = args
    fulldir = fdir / file
    newdir = fdir.relative_to(inDir)
    outdirectory = outDir / newdir / file.replace(".lua.bytes", ".lua")
    outdirectory.parent.mkdir(parents=True, exist_ok=True)
    command = ['java', '-cp', './class', 'unluac.Main', str(fulldir)]
    with open(outdirectory, 'w') as out_file:
        subprocess.run(command, stdout=out_file, stderr=subprocess.PIPE)

if __name__ == '__main__':
    with ProcessPoolExecutor(max_workers=os.cpu_count()) as executor:
        for fdir, _, files in os.walk(inDir):
            fdir = Path(fdir)
            executor.map(process_file, [(fdir, file) for file in files])