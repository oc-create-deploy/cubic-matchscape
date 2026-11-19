using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using PlayableGames;

public sealed class MatchscapeGame : MonoBehaviour
{
    public Material cubeMaterial;
    [Serializable] public class RoundState {public int level=1;public List<int> selected=new List<int>();public List<int> tray=new List<int>();}
    private RoundState state=new RoundState();
    private readonly Dictionary<int,MatchscapePiece> pieces=new Dictionary<int,MatchscapePiece>();
    private readonly List<GameObject> visuals=new List<GameObject>();
    private readonly Text[] trayLabels=new Text[7];
    private readonly Color[] palette={new Color(.13f,.7f,.95f),new Color(.98f,.48f,.25f),new Color(.4f,.85f,.48f),new Color(.72f,.45f,.95f),new Color(1f,.76f,.2f),new Color(.96f,.4f,.65f)};
    private Text heading,progress,message;
    private Button action;
    private Camera boardCamera;
    private int total;
    private bool won,lost;
    public int Remaining => pieces.Count;
    public int TrayCount => state.tray.Count;
    public bool Won => won;
    public bool Lost => lost;
    private void Start()
    {
        Application.targetFrameRate=60;
        try {string json=PlayerPrefs.GetString("matchscape.round.v1","");if(!string.IsNullOrEmpty(json)) state=JsonUtility.FromJson<RoundState>(json)??new RoundState();}catch(Exception){state=new RoundState();}
        if(state.level<1 || state.selected==null || state.tray==null || state.tray.Count>7) state=new RoundState();
        var backdrop=new GameObject("Background camera").AddComponent<Camera>();backdrop.depth=-10;backdrop.clearFlags=CameraClearFlags.SolidColor;backdrop.backgroundColor=new Color(.025f,.055f,.105f);backdrop.cullingMask=0;
        boardCamera=new GameObject("Puzzle camera").AddComponent<Camera>();boardCamera.tag="MainCamera";
        boardCamera.transform.position=new Vector3(5,10,-8);boardCamera.transform.LookAt(new Vector3(0,0,3));boardCamera.orthographic=true;boardCamera.orthographicSize=6;
        boardCamera.rect=new Rect(0,.24f,1,.53f);boardCamera.backgroundColor=new Color(.045f,.09f,.16f);boardCamera.depth=0;
        var light=new GameObject("Soft key light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(45,-35,0);
        RenderSettings.ambientLight=new Color(.65f,.7f,.8f);
        var panel=NativeUi.Create("Cubic Matchscape interface",false);
        heading=NativeUi.Label(panel,"CUBIC MATCHSCAPE",.04f,.88f,.92f,.065f,30);
        progress=NativeUi.Label(panel,"",.06f,.82f,.88f,.045f,20);
        NativeUi.Label(panel,"Tap cubes. Match three of one color.\nKeep your seven-slot tray from filling.",.07f,.735f,.86f,.07f,17);
        for(int i=0;i<7;i++) trayLabels[i]=NativeUi.Label(panel,"",.04f+i*.133f,.14f,.12f,.06f,28);
        message=NativeUi.Label(panel,"",.04f,.205f,.92f,.045f,20);
        action=NativeUi.Button(panel,"RESTART PUZZLE",.14f,.035f,.72f,.075f,()=>{if(won) state.level++;state.selected.Clear();state.tray.Clear();BuildRound();});
        BuildRound();
    }
    private void BuildRound()
    {
        foreach(GameObject go in visuals) Destroy(go);visuals.Clear();pieces.Clear();won=lost=false;
        int groups=Mathf.Clamp(state.level+2,3,6);total=groups*3;var numbers=new List<int>();
        for(int i=0;i<groups;i++) for(int j=0;j<3;j++) numbers.Add(i);
        var random=new System.Random(state.level*1543);
        for(int i=numbers.Count-1;i>0;i--) {int j=random.Next(i+1);int temp=numbers[i];numbers[i]=numbers[j];numbers[j]=temp;}
        var unique=new HashSet<int>(state.selected);if(unique.Count!=state.selected.Count || unique.Count>total) {state.selected.Clear();state.tray.Clear();}
        for(int i=0;i<total;i++)
        {
            if(state.selected.Contains(i)) continue;
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Cube "+i;go.transform.position=new Vector3((i%3-1)*1.45f,0,(i/3)*1.35f);go.transform.rotation=Quaternion.Euler(0,(i%3-1)*8,0);go.transform.localScale=Vector3.one*.9f;
            var material=new Material(cubeMaterial!=null ? cubeMaterial : new Material(Shader.Find("Standard")));material.color=palette[numbers[i]];if(material.HasProperty("_BaseColor")) material.SetColor("_BaseColor",palette[numbers[i]]);go.GetComponent<Renderer>().material=material;
            var piece=go.AddComponent<MatchscapePiece>();piece.id=i;piece.group=numbers[i];pieces.Add(i,piece);visuals.Add(go);
            var text=new GameObject("Color number").AddComponent<TextMesh>();text.transform.SetParent(go.transform,false);text.transform.localPosition=new Vector3(0,.65f,0);text.transform.rotation=boardCamera.transform.rotation;text.font=NativeUi.Font;text.GetComponent<Renderer>().material=NativeUi.Font.material;text.text=(numbers[i]+1).ToString();text.fontSize=64;text.characterSize=.11f;text.anchor=TextAnchor.MiddleCenter;text.color=Color.white;
        }
        Evaluate();Save();
    }
    private void Update()
    {
        if(won || lost) return;
        bool pressed=Input.GetMouseButtonDown(0);Vector2 point=Input.mousePosition;int finger=-1;
        if(Input.touchCount>0) {Touch t=Input.GetTouch(0);pressed=t.phase==TouchPhase.Began;point=t.position;finger=t.fingerId;}
        if(!pressed) return;
        if(EventSystem.current!=null && (finger<0 ? EventSystem.current.IsPointerOverGameObject() : EventSystem.current.IsPointerOverGameObject(finger))) return;
        Ray ray=boardCamera.ScreenPointToRay(point);
        if(Physics.Raycast(ray,out RaycastHit hit)) {var piece=hit.collider.GetComponent<MatchscapePiece>();if(piece!=null) Select(piece.id);}
    }
    public void Select(int id)
    {
        if(won || lost || !pieces.TryGetValue(id,out MatchscapePiece piece)) return;
        int group=piece.group;state.selected.Add(id);state.tray.Add(group);pieces.Remove(id);Destroy(piece.gameObject);
        int matches=state.tray.FindAll(x=>x==group).Count;
        if(matches>=3) {for(int i=0;i<3;i++) state.tray.Remove(group);}
        Evaluate();Save();
    }
    private void Evaluate()
    {
        won=pieces.Count==0 && state.tray.Count==0;lost=!won && state.tray.Count>=7;
        progress.text="PUZZLE "+state.level+"  •  "+pieces.Count+" cubes left";
        for(int i=0;i<7;i++) {trayLabels[i].text=i<state.tray.Count ? (state.tray[i]+1).ToString() : "·";trayLabels[i].color=i<state.tray.Count ? palette[state.tray[i]] : new Color(.35f,.45f,.6f);}
        message.text=won ? "Perfect match — puzzle complete!" : lost ? "Tray full. Try a different order." : "TRAY  "+state.tray.Count+" / 7";
        action.GetComponentInChildren<Text>().text=won ? "NEXT PUZZLE" : lost ? "TRY AGAIN" : "RESTART PUZZLE";
    }
    private void Save() {PlayerPrefs.SetString("matchscape.round.v1",JsonUtility.ToJson(state));PlayerPrefs.Save();}
    private void OnApplicationPause(bool paused) {if(paused) Save();}
    private void OnApplicationQuit() {Save();}
}
