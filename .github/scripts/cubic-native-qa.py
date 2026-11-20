"""Drive normal native touch input; QA snapshots are read-only observations."""
import json, os, pathlib, subprocess, time
out = pathlib.Path('screenshots'); out.mkdir(exist_ok=True)
udid = os.environ['UDID']; bundle = os.environ['APP_BUNDLE_ID']
container = pathlib.Path(subprocess.check_output(['xcrun','simctl','get_app_container',udid,bundle,'data'],text=True).strip())
seq = 0
import importlib.util
spec=importlib.util.spec_from_file_location('touchbridge',pathlib.Path(__file__).with_name('native-touch-bridge.py'))
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
bridge=module.TouchBridge(out,udid,bundle)

def state():
    deadline = time.time()+90
    while time.time()<deadline:
        for p in container.rglob('qa-state.json'):
            try:
                s=json.loads(p.read_text())
                assert not s['errors'],s['errors']
                if s["frame"] >= 30: return s
            except (json.JSONDecodeError,FileNotFoundError): pass
        time.sleep(.5)
    probe_errors=list(container.rglob('qa-recorder-error.txt'))
    if probe_errors:
        detail=probe_errors[0].read_text()
        (out/'qa-recorder-error.txt').write_text(detail)
        raise AssertionError('Native diagnostic recorder failed: '+detail)
    raise AssertionError('Read-only native snapshot missing')

def capture(label):
    (out/(label+'.json')).write_text(json.dumps(state(),indent=2))
    subprocess.run(['xcrun','simctl','io',udid,'screenshot',str(out/(label+'.png'))],check=True)

def point(item):
    assert 0<item['x']<1 and 0<item['y']<1,item
    return f"{round(item['x']*100)}%, {round(item['y']*100)}%"

def swipe_point(item):
    return f"{item['x']*100:.4f}%, {item['y']*100:.4f}%"

def action(body):
    global seq
    seq+=1
    p=out/f'input-{seq:03}.yaml'
    p.write_text(f'appId: {bundle}\n---\n'+body+'\n')
    if bridge.proc is not None and body.startswith("- tapOn:"):
        bridge.tap_direct(body.split('"')[1])
    else: bridge.action(body)
    time.sleep(.1)

def tap(text):
    choices=[i for i in state()['items'] if i['kind']=='button' and i['interactable'] and text.casefold() in (i.get('text','').strip().casefold(),i['name'].strip().casefold())]
    assert choices,('Button missing',text)
    action('- tapOn:\n    point: "'+point(choices[0])+'"')

def val(key):return next(v['value'] for v in state()['values'] if v['key']==key)

def cubes():return [i for i in state()['items'] if i['kind']=='cube']
def choose(item):
    before=int(val('MatchscapeGame.Remaining'))
    action('- tapOn:\n    point: "'+point(item)+'"')
    deadline=time.time()+3
    while int(val('MatchscapeGame.Remaining'))==before and time.time()<deadline:time.sleep(.1)
    assert int(val('MatchscapeGame.Remaining'))==before-1,('Cube tap did not select',item)
def solve():
    while cubes():
        group=cubes()[0]['group']
        for item in [i for i in cubes() if i['group']==group]:choose(item)
    assert val('MatchscapeGame.Won')=='True' and int(val('MatchscapeGame.TrayCount'))==0

def cold_launch():
    subprocess.run(['xcrun','simctl','terminate',udid,bundle],check=True)
    for p in container.rglob('qa-state.json'):p.unlink()
    subprocess.run(['xcrun','simctl','launch',udid,bundle],check=True);time.sleep(4)

action('- tapOn:\n    point: "95%, 95%"')
capture('01-board')
assert len(cubes())==9,('Initial first puzzle incorrect',len(cubes()))
solve();capture('02-first-win')
tap('NEXT PUZZLE');time.sleep(1)
assert len(cubes())==12,'Second puzzle did not advance'
capture('03-second-puzzle')
# Seven mixed selections with at most two of any color must fill, not clear, the tray.
counts={}
for item in list(cubes()):
    group=item['group']
    if counts.get(group,0)>=2:continue
    choose(item);counts[group]=counts.get(group,0)+1
    if sum(counts.values())==7:break
assert val('MatchscapeGame.Lost')=='True' and int(val('MatchscapeGame.TrayCount'))==7
capture('04-tray-full')
tap('TRY AGAIN');time.sleep(1)
assert len(cubes())==12 and int(val('MatchscapeGame.TrayCount'))==0
# Persist an actual partially played round through a native process restart.
choose(cubes()[0]);choose(cubes()[0]);saved=val('MatchscapeGame.state');ids=sorted(i['id'] for i in cubes())
capture('05-partial')
cold_launch()
assert val('MatchscapeGame.state')==saved and sorted(i['id'] for i in cubes())==ids,'Partial board/tray did not restore exactly'
capture('06-restored')
solve();capture('07-second-win')
tap('NEXT PUZZLE');time.sleep(1)
assert len(cubes())==15,'Third puzzle not reached'
cold_launch();assert len(cubes())==15,'Puzzle progression did not survive restart'
capture('08-third-restored')
(out/'result.json').write_text(json.dumps({'nativeCubeSelection':True,'tripleClears':True,'firstAndSecondPuzzleWins':True,'sevenSlotLoss':True,'retry':True,'exactPartialBoardTrayColdLaunch':True,'thirdPuzzleProgressColdLaunch':True},indent=2))
