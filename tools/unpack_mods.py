"""Extract a data-only Mods ZIP into a new workspace after validating all entries."""
from pathlib import Path, PurePosixPath
import stat
import sys
import zipfile

def unpack(archive, destination):
    destination=Path(destination).resolve()
    if destination.exists():
        raise ValueError('Extraction destination already exists')
    with zipfile.ZipFile(archive) as z:
        total=0;seen=set()
        for entry in z.infolist():
            parts=PurePosixPath(entry.filename).parts
            if not parts or parts[0]!='Mods' or any(p in {'.','..'} or ':' in p or '\\' in p for p in parts) or entry.filename.startswith('/'):
                raise ValueError('ZIP must contain only Mods/...: '+entry.filename)
            if stat.S_ISLNK(entry.external_attr >> 16):
                raise ValueError('ZIP links are forbidden')
            key=entry.filename.rstrip('/').lower()
            if key in seen:raise ValueError('Duplicate ZIP entry')
            seen.add(key);total+=entry.file_size
            if total>8*1024**3 or len(seen)>20000:raise ValueError('ZIP is too large')
        # No files are executed. The publisher validates Windows names separately.
        z.extractall(destination)
    return destination/'Mods'

if __name__=='__main__':
    unpack(sys.argv[1],sys.argv[2])
