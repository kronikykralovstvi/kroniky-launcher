"""Prepare a client-only release; publish assets first and activate its manifest last."""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

REPO = 'kronikykralovstvi/kroniky-launcher'

def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as source:
        for block in iter(lambda: source.read(4*1024*1024), b''):
            h.update(block)
    return h.hexdigest()

def prepare(source, version, output):
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._-]{0,63}', version):
        raise ValueError('Invalid version: use e.g. 2026.11.03.1')
    if source.name.lower() != 'mods' or not source.is_dir():
        raise ValueError('Select the curated client Mods folder, not the whole server package')
    files = sorted(p for p in source.rglob('*') if p.is_file())
    if not files:
        raise ValueError('No mod files found')
    output.mkdir(parents=True, exist_ok=True)
    assets = output/'assets'
    assets.mkdir(exist_ok=True)
    manifest = {'schemaVersion': 1, 'version': version, 'files': []}
    seen = set()
    for path in files:
        rel = path.relative_to(source)
        if len(rel.parts) < 2 or rel.parts[0].lower() == 'kcdmp' or rel.parts[0].startswith('.'):
            raise ValueError('Only individual server mod folders are allowed: '+str(rel))
        for parent in [path, *path.parents]:
            if parent.is_symlink() or parent.is_junction():
                raise ValueError('Links/junctions are not allowed: '+str(parent))
            if parent == source:
                break
        if any(re.search(r'[<>:"\\|?*\x00-\x1f]', part) or part.endswith((' ', '.')) or re.match(r'^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)',part,re.I) for part in rel.parts):
            raise ValueError('Invalid Windows filename: '+str(rel))
        name = 'Mods/'+rel.as_posix()
        if name.lower() in seen:
            raise ValueError('Duplicate path: '+name)
        seen.add(name.lower())
        if path.stat().st_size >= 2*1024**3:
            raise ValueError('GitHub asset must be below 2 GiB: '+name)
        digest = sha(path)
        asset = digest+'.bin'
        dest = assets/asset
        if not dest.exists():
            shutil.copy2(path, dest)
        if sha(dest) != digest:
            raise ValueError('Prepared asset differs: '+name)
        manifest['files'].append({'path':name, 'url':f'https://github.com/{REPO}/releases/download/mods-{version}/{asset}', 'sha256':digest, 'size':path.stat().st_size})
    (output/'mods-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n',encoding='utf-8')
    return manifest

def run(gh, *args):
    return subprocess.check_output([gh, *args], text=True, encoding='utf-8')

def publish(gh, prepared, manifest):
    tag = 'mods-'+manifest['version']
    # A fresh tag makes the published payload immutable. Never overwrite a live release.
    probe = subprocess.run([gh,'release','view',tag,'--repo',REPO],capture_output=True)
    if probe.returncode == 0:
        raise ValueError('This release already exists. Use a new version; no live asset was replaced.')
    run(gh,'api',f'repos/{REPO}')  # fail early on missing access/network
    pointer=subprocess.run([gh,'api',f'repos/{REPO}/contents/mods-manifest.json'],capture_output=True,text=True,encoding='utf-8')
    if pointer.returncode != 0 and 'HTTP 404' not in pointer.stderr:
        raise ValueError('Cannot read the current manifest: '+pointer.stderr)
    previous_sha=json.loads(pointer.stdout)['sha'] if pointer.returncode==0 else None
    run(gh,'release','create',tag,'--repo',REPO,'--target','main','--draft','--title','Mody '+manifest['version'],'--notes','Klientské mody pro server Kroniky Království. Spravováno launcherem 1.0.8+.')
    for file in sorted((prepared/'assets').iterdir()):
        print('Uploading',file.name,flush=True)
        run(gh,'release','upload',tag,str(file),'--repo',REPO)
    run(gh,'release','upload',tag,str(prepared/'mods-manifest.json'),'--repo',REPO)
    finish(gh, prepared, manifest, previous_sha)

def finish(gh, prepared, manifest, previous_sha):
    tag='mods-'+manifest['version']
    # A draft's tag may not exist yet; resolve its release ID through gh first.
    info=json.loads(run(gh,'release','view',tag,'--repo',REPO,'--json','apiUrl'))
    remote=json.loads(run(gh,'api',info['apiUrl']))
    uploaded={a['name']:a for a in remote['assets']}
    for item in manifest['files']:
        asset=item['url'].rsplit('/',1)[1]
        if asset not in uploaded or uploaded[asset]['size'] != item['size']:
            raise ValueError('Release verification failed: '+asset)
        if uploaded[asset].get('digest') != 'sha256:'+item['sha256']:
            raise ValueError('GitHub asset SHA-256 verification failed: '+asset)
    run(gh,'release','edit',tag,'--repo',REPO,'--draft=false','--latest=false')
    # CAS against the state at publication start: do not overwrite an intervening release.
    payload={'message':'Publish mod pack '+manifest['version'],'content':base64.b64encode((prepared/'mods-manifest.json').read_bytes()).decode(),'branch':'main'}
    if previous_sha is not None:
        payload['sha']=previous_sha
    with tempfile.TemporaryDirectory() as temp:
        request=Path(temp)/'request.json';request.write_text(json.dumps(payload),encoding='utf-8')
        run(gh,'api','--method','PUT',f'repos/{REPO}/contents/mods-manifest.json','--input',str(request))
    print('Published:',f'https://github.com/{REPO}/releases/tag/{tag}')
    print('Players receive this pack when they next check updates or press Play.')

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mods',type=Path,required=True,help='Curated Mods directory containing only server client mods')
    parser.add_argument('--version',required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--publish',action='store_true',help='Upload to GitHub and activate the manifest after verification')
    parser.add_argument('--gh',default='gh')
    args=parser.parse_args()
    if args.output.exists() and any(args.output.iterdir()):
        raise ValueError('Output must be a new or empty directory')
    manifest=prepare(args.mods.resolve(),args.version,args.output.resolve())
    print('Prepared',len(manifest['files']),'files; bytes:',sum(f['size'] for f in manifest['files']))
    if args.publish:
        publish(args.gh,args.output.resolve(),manifest)
    else:
        print('Preparation only. No GitHub changes.')

if __name__=='__main__':
    main()
