import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('publisher',Path(__file__).parents[1]/'tools/publish_mods.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)

class PublisherTests(unittest.TestCase):
    def test_draft_verification_before_activation(self):
        for valid in [False,True]:
            with self.subTest(valid=valid), tempfile.TemporaryDirectory() as d:
                root=Path(d);mods=root/'Mods';(mods/'armor').mkdir(parents=True)
                (mods/'armor/mod.manifest').write_bytes(b'example')
                prepared=root/'prepared';manifest=module.prepare(mods,'1',prepared)
                row=manifest['files'][0];calls=[]
                def fake(gh,*args):
                    calls.append(args)
                    if args[:2]==('release','view'):return json.dumps({'apiUrl':'https://api.github.com/releases/123'})
                    if args==('api','https://api.github.com/releases/123'):
                        return json.dumps({'assets':[{'name':row['url'].rsplit('/',1)[1],'size':row['size'],'digest':'sha256:'+row['sha256'] if valid else 'bad'}]})
                    return '{}'
                with patch.object(module,'run',side_effect=fake):
                    if valid:module.finish('gh',prepared,manifest,None)
                    else:
                        with self.assertRaises(ValueError):module.finish('gh',prepared,manifest,None)
                edits=[i for i,a in enumerate(calls) if a[:2]==('release','edit')]
                puts=[i for i,a in enumerate(calls) if a[:3]==('api','--method','PUT')]
                self.assertEqual(bool(edits),valid);self.assertEqual(bool(puts),valid)
                if valid:self.assertLess(edits[0],puts[0])
    def test_client_only_and_hashes(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);mods=root/'Mods';(mods/'armor/Data').mkdir(parents=True)
            (mods/'armor/Data/test.pak').write_bytes(b'example')
            (mods/'armor/mod.manifest').write_bytes(b'example')
            manifest=module.prepare(mods,'2026.11.03.1',root/'prepared')
            self.assertEqual(len(manifest['files']),2)
            self.assertEqual(len(list((root/'prepared/assets').iterdir())),1)
            self.assertEqual(manifest['files'][0]['size'],7)
            self.assertEqual(manifest['files'][0]['sha256'],module.sha(mods/'armor/Data/test.pak'))
            self.assertEqual(json.loads((root/'prepared/mods-manifest.json').read_text()),manifest)
    def test_rejects_wrong_root_empty_and_kcdmp(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);mods=root/'Mods';mods.mkdir()
            with self.assertRaises(ValueError):module.prepare(root,'1',root/'out')
            with self.assertRaises(ValueError):module.prepare(mods,'1',root/'out')
            (mods/'KcdMp').mkdir();(mods/'KcdMp/mod.manifest').write_text('do not distribute')
            with self.assertRaises(ValueError):module.prepare(mods,'1',root/'out')
            with self.assertRaises(ValueError):module.prepare(mods,'../bad',root/'out')

if __name__=='__main__':unittest.main()
