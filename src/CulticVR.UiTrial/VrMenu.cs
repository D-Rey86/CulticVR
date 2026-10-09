using System;
using System.Collections.Generic;
using System.Linq;
using CulticVR.AimPreview;
using CulticVR.WorldStereoTrial;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CulticVR.UiTrial
{
    public sealed partial class UiTrialPlugin
    {
        private Harmony? _menuHarmony;
        private void InitializeVrMenu()
        {
            _menuHarmony = new Harmony("culticvr.uitrial.vr-menu");
            _menuHarmony.Patch(AccessTools.Method(typeof(scrMenuControllerV2),"Start"),
                postfix:new HarmonyMethod(AccessTools.Method(typeof(UiTrialPlugin),nameof(MenuStarted))));
        }
        private static void MenuStarted(scrMenuControllerV2 __instance)
        {
            try { __instance.gameObject.AddComponent<VrMenuHost>().Build(__instance); }
            catch(Exception e) { Debug.LogError("CULTIC VR menu setup failed: "+e); }
        }
        private void RemoveVrMenu() { VrSettings.Save(); _menuHarmony?.UnpatchSelf(); }
    }

    // Native menu owns selection, pause, return, scrolling, highlight and sound.
    // This component creates only additional rows in that same canvas/panel.
    public sealed class VrMenuHost : MonoBehaviour
    {
        private scrMenuControllerV2 _menu = null!;
        private readonly List<VrMenuRow> _rows = new List<VrMenuRow>();
        private Menu _vr = null!;
        private GameObject _staging = null!;
        private scrMenuSlider _sliderTemplate = null!;
        private scrMenuToggle _toggleTemplate = null!;
        private scrMenuButton _buttonTemplate = null!;
        private bool _wasOpen;
        private float _saveAt;
        private bool _calibrationDirty;
        private TextMeshProUGUI _resolutionText = null!;
        private int _eyeWidth=-1, _eyeHeight=-1;
        private Texture2D? _radioTexture;
        private Sprite? _radioSprite;
        private Sprite? _radioSelectedSprite;
        private Material? _headerMaterial;
        private float _shownResolution = -1f;

        internal void Build(scrMenuControllerV2 menu)
        {
            _menu=menu;
            if(menu.menus.Any(m => (int)m.menuID >= VrSettings.MenuId)) throw new InvalidOperationException("VR menu ID already assigned");
            var video=menu.getMenuByID(scrMenuControllerV2.MenuID.menuVideo);
            _sliderTemplate=video.menuElements.OfType<scrMenuSlider>().First(e=>e.prefsString=="gamma");
            _toggleTemplate=video.menuElements.OfType<scrMenuToggle>().First();
            var main=menu.getMenuByID(menu.isMain?scrMenuControllerV2.MenuID.menuMain:scrMenuControllerV2.MenuID.menuPause);
            _buttonTemplate=main.menuElements.OfType<scrMenuButton>().First(e=>e.buttonMenuTarget==scrMenuControllerV2.MenuID.menuOptions);
            _staging=new GameObject("CulticVR Menu Construction"); _staging.SetActive(false);
            var group=Instantiate(video.menuGroup,_staging.transform,false);
            group.name="CulticVR Options";
            for(int i=group.transform.childCount-1;i>=0;i--) DestroyImmediate(group.transform.GetChild(i).gameObject);
            _vr=new Menu { menuName="VR Options",menuID=(scrMenuControllerV2.MenuID)VrSettings.MenuId,
                menuGroup=group,menuRect=(RectTransform)group.transform,menuPanel=video.menuPanel,
                returnMenu=main.menuID,returnElement=null!,showHelpPanel=true,closeAllMenus=true };
            Header("Picture");
            Slider("Resolution",()=>VrSettings.Resolution.Value,v=>VrSettings.Set(VrSettings.Resolution,v),.5f,2f,.05f,1f,"0%",
                "50-200% of the runtime's recommended resolution. Restart the game to apply. Supersampling uses more GPU time and memory; current dimensions are shown below.");
            _resolutionText=Header("Current resolution: waiting for XR").controlMainText;
            Slider("Stereo Separation",()=>VrSettings.Separation.Value,v=>VrSettings.Set(VrSettings.Separation,v),.5f,1.5f,.05f,1f,"0%",
                "100% uses your headset's measured eye separation. Adjusts the world eye baseline; scope sighting keeps your selected eye fixed.");
            Slider("Brightness",()=>scrGameControl.Instance.prefsList["gamma"],v=> {
                scrGameControl.Instance.updatePref("gamma",v); _menu.prefsChanged=true;
            },0f,1f,.025f,.5f,"0%","Uses CULTIC's brightness value. 50% is neutral; other values enable native colour grading on the stereo world and add GPU work.");
            Header("Controls");
            Toggle("Teleport",()=>VrSettings.Teleport.Value,v=>VrSettings.Set(VrSettings.Teleport,v),false,
                "Hold the movement stick forward to aim; release to teleport. Green is a valid destination. Native collision, ceiling clearance and a clear travel path are required.");
            Toggle("Snap Turn",()=>VrSettings.SnapTurn.Value,v=>VrSettings.Set(VrSettings.SnapTurn,v),false,
                "One turn per horizontal stick deflection. Return the stick to neutral for another turn.");
            Radio();
            Toggle("Controller Mode",()=>!VrSettings.MotionControls,v=>VrSettings.Set(VrSettings.ControllerMode,v),false,
                "Checked: regular gamepad with headset aim. Unchecked (default): motion controls. Motion controllers can still open and navigate menus in either mode.");
            Toggle("Left Hand Mode",()=>VrSettings.LeftWeapon,v=>VrSettings.Set(VrSettings.LeftHand,v),false,
                "Weapon and aiming hand on the left; off-hand item and wrist display on the right. Button assignments are unchanged.");
            Toggle("Swap Movement/Turn Sticks",()=>VrSettings.SticksSwapped,v=>VrSettings.Set(VrSettings.SwapSticks,v),false,
                "Swap only movement and turning axes. Applies to motion controllers; regular gamepad keeps the game's bindings.");
            Header("Weapons");
            var axisNames=new[]{"Position X","Position Y","Position Z","Rotation X","Rotation Y","Rotation Z"};
            for(int i=0;i<6;i++) {
                int axis=i;
                Slider("Weapon "+axisNames[i],()=>AimPreviewPlugin.GetCalibration(axis),v=> {
                    AimPreviewPlugin.SetCalibration(axis,v); _calibrationDirty=true;
                },i<3?-.5f:-180f,i<3?.5f:180f,i<3?.005f:1f,AimPreviewPlugin.DefaultCalibration(i),i<3?"0.000 m":"0 deg",
                    "Global weapon calibration in the selected hand's local coordinates. Muzzle and scope landmarks follow these same offsets.");
            }
            Slider("Weapon Sprite Width",()=>VrSettings.WeaponWidth,v=>VrSettings.Set(VrSettings.SpriteWidth,v),.5f,2f,.05f,1f,"0%","Artwork width; muzzle and scope points scale with it.");
            Slider("Weapon Sprite Height",()=>VrSettings.WeaponHeight,v=>VrSettings.Set(VrSettings.SpriteHeight,v),.5f,2f,.05f,1f,"0%","Artwork height; muzzle and scope points scale with it.");
            Button("Reset All Values",()=> {
                VrSettings.Reset(); AimPreviewPlugin.ResetCalibration(); _calibrationDirty=true;
                scrGameControl.Instance.updatePref("gamma",.5f); _menu.prefsChanged=true;
                foreach(var row in _rows) row.Refresh(); Save();
            },"Restore all settings in this menu and weapon calibration to defaults.");
            Button("Back",()=>_menu.tryReturnToPrevious(),"Return to the previous game menu.");
            _vr.menuElements=_rows.Where(r=>!r.ignoreElement).Cast<scrMenuElement>().ToArray();
            _vr.defaultMenuObject=_vr.menuElements[0];
            group.SetActive(false); group.transform.SetParent(video.menuGroup.transform.parent,false);
            menu.menus=menu.menus.Concat(new[]{_vr}).ToArray();
            Inject(main);
            // Both lists can exist in the scene. Add the same entry to the
            // other list without changing any native button's destination.
            var other=menu.getMenuByID(menu.isMain?scrMenuControllerV2.MenuID.menuPause:scrMenuControllerV2.MenuID.menuMain);
            if(other!=null && other!=main) Inject(other);
            Destroy(_staging);
        }
        private void Inject(Menu parent)
        {
            var options=parent.menuElements.OfType<scrMenuButton>().First(e=>e.buttonMenuTarget==scrMenuControllerV2.MenuID.menuOptions);
            var row=Clone(options.gameObject,parent.menuGroup.transform,"VR Options");
            row.Action=()=> {
                _vr.returnMenu=parent.menuID; _vr.returnElement=row;
                _menu.changeMenuByID(_vr.menuID);
                foreach(var r in _rows) r.Refresh();
            };
            row.Help("Configure headset picture, controls and weapons.");
            row.transform.SetSiblingIndex(options.transform.GetSiblingIndex()+1);
            row.gameObject.SetActive(true);
            parent.menuElements=parent.menuGroup.GetComponentsInChildren<scrMenuElement>(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)parent.menuGroup.transform);
        }
        private VrMenuRow Clone(GameObject template,Transform parent,string label)
        {
            // Construct below an inactive parent so native Start/OnEnable cannot
            // write copied preference keys or subscribe duplicate menu events.
            var go=Instantiate(template,_staging.transform,false); go.SetActive(false); go.name="VR "+label;
            var old=go.GetComponent<scrMenuElement>();
            var text=old.controlMainText; var backing=old.elementBacking;
            var slider=old as scrMenuSlider; var toggle=old as scrMenuToggle;
            var sliderBounds=slider?.sliderBounds; var sliderBar=slider?.sliderBar; var sliderImage=slider?.sliderImage; var sliderText=slider?.sliderValueText;
            var toggleImage=toggle?.toggleSprite; var toggleSprites=toggle?.toggleSprites;
            var hover=old.hoverAudio; var select=old.selectAudio; var press=old.pressAudio;
            DestroyImmediate(old);
            foreach(var loc in go.GetComponentsInChildren<scrTextLocalization>(true)) DestroyImmediate(loc);
            var row=go.AddComponent<VrMenuRow>(); row.systemMenu=_menu; row.rectTransform=(RectTransform)go.transform;
            row.controlMainText=text; row.elementBacking=backing; row.controlMainText.text=label;
            row.hoverAudio=hover; row.selectAudio=select; row.pressAudio=press;
            row.prefsString=""; row.selectInvoke=""; row.canBeReset=false;
            row.SliderBounds=sliderBounds; row.SliderBar=sliderBar; row.SliderImage=sliderImage; row.ValueText=sliderText;
            row.ToggleImage=toggleImage; row.ToggleSprites=toggleSprites; row.Changed=Changed;
            go.transform.SetParent(parent,false); go.SetActive(true);
            return row;
        }
        private VrMenuRow Header(string label)
        {
            var row=Clone(_sliderTemplate.gameObject,_vr.menuGroup.transform,label);
            var value=row.transform.Find("panelValueBackDrop"); if(value!=null) value.gameObject.SetActive(false);
            row.ignoreElement=true; row.canBeReset=false;
            row.controlMainText.rectTransform.sizeDelta=new Vector2(490,22);
            row.controlMainText.fontStyle=FontStyles.Bold;
            row.controlMainText.fontWeight=FontWeight.Bold;
            row.controlMainText.enableAutoSizing=false;
            row.controlMainText.fontSize=20;
            var layout=row.GetComponent<LayoutElement>();
            layout.minHeight=layout.preferredHeight=24;
            if (_headerMaterial==null) {
                _headerMaterial=new Material(row.controlMainText.fontSharedMaterial){name="CulticVR Category Bold"};
                if (_headerMaterial.HasProperty("_WeightBold")) _headerMaterial.SetFloat("_WeightBold",1.5f);
            }
            row.controlMainText.fontSharedMaterial=_headerMaterial;
            _rows.Add(row); return row;
        }
        private void Slider(string label,Func<float> get,Action<float> set,float min,float max,float step,float def,string format,string help)
        {
            var row=Clone(_sliderTemplate.gameObject,_vr.menuGroup.transform,label);
            row.GetValue=get; row.SetValue=set; row.Min=min; row.Max=max; row.Step=step; row.Default=def; row.Format=format;
            // Enough room for signed metre/degree values, while retaining the
            // native bar/backdrop and row dimensions.
            row.ValueText!.rectTransform.sizeDelta=new Vector2(82,16);
            row.SliderBounds!.sizeDelta=new Vector2(54,8);
            row.SliderBounds.anchoredPosition=new Vector2(-82,0);
            row.Help(help); row.Refresh(); _rows.Add(row);
        }
        private void Toggle(string label,Func<bool> get,Action<bool> set,bool def,string help)
        {
            var row=Clone(_toggleTemplate.gameObject,_vr.menuGroup.transform,label);
            row.GetToggle=get; row.SetToggle=set; row.Default=def?1:0;
            row.controlMainText.rectTransform.sizeDelta=new Vector2(350,16);
            row.Help(help); row.Refresh(); _rows.Add(row);
        }
        private void Button(string label,Action action,string help)
        {
            var row=Clone(_buttonTemplate.gameObject,_vr.menuGroup.transform,label);
            row.rectTransform.sizeDelta=new Vector2(504,20);
            row.Action=action; row.Help(help); _rows.Add(row);
        }
        private void Radio()
        {
            var row=Clone(_toggleTemplate.gameObject,_vr.menuGroup.transform,"Snap Turn Amount");
            row.ToggleImage!.gameObject.SetActive(false);
            var backdrop=row.transform.Find("panelValueBackDrop") as RectTransform;
            backdrop!.sizeDelta=new Vector2(230,16);
            row.RadioValues=new[]{15,30,45,60,90}; row.RadioImages=new Image[5];
            row.GetValue=()=>VrSettings.SnapDegrees.Value;
            row.SetValue=v=>VrSettings.Set(VrSettings.SnapDegrees,(int)v);
            _radioTexture=new Texture2D(64,32,TextureFormat.RGBA32,false){name="CulticVR Radio Ring"};
            for(int y=0;y<32;y++) for(int x=0;x<64;x++) {
                float r=new Vector2(x%32-15.5f,y-15.5f).magnitude;
                _radioTexture.SetPixel(x,y,new Color(1,1,1,(r>=11&&r<=15)||(x>=32&&r<=6)?1:0));
            }
            _radioTexture.Apply(); _radioSprite=Sprite.Create(_radioTexture,new Rect(0,0,32,32),new Vector2(.5f,.5f));
            _radioSelectedSprite=Sprite.Create(_radioTexture,new Rect(32,0,32,32),new Vector2(.5f,.5f));
            row.RadioEmpty=_radioSprite; row.RadioFilled=_radioSelectedSprite; row.RadioTexts=new TextMeshProUGUI[5];
            for(int i=0;i<5;i++) {
                var go=new GameObject("Radio "+row.RadioValues[i],typeof(RectTransform),typeof(Image));
                var rt=(RectTransform)go.transform; rt.SetParent(backdrop,false); rt.sizeDelta=new Vector2(10,10);
                rt.anchoredPosition=new Vector2(-98+i*46,0);
                var image=go.GetComponent<Image>(); image.sprite=_radioSprite; row.RadioImages[i]=image;
                var text=Instantiate(row.controlMainText,rt,false); text.text=row.RadioValues[i].ToString(); text.fontSize=12;
                row.RadioTexts[i]=text;
                text.rectTransform.pivot=new Vector2(0,.5f); text.rectTransform.anchorMin=text.rectTransform.anchorMax=new Vector2(.5f,.5f);
                text.rectTransform.anchoredPosition=new Vector2(8,0); text.rectTransform.sizeDelta=new Vector2(28,16);
            }
            row.Help("Choose 15, 30, 45, 60 or 90 degrees with left/right. Confirm selects the indicated amount.");
            row.Refresh(); _rows.Add(row);
        }
        private void Changed() { _saveAt=Time.unscaledTime+.5f; }
        private void Update()
        {
            bool open=_menu!=null && _menu.currentMenu==_vr && _menu.menuState!=scrMenuControllerV2.MenuState.menuStateClosed;
            if(_wasOpen && !open) Save(); _wasOpen=open;
            if((VrSettings.Dirty||_calibrationDirty) && Time.unscaledTime>=_saveAt) Save();
            if(!open) return;
            int w=Mathf.RoundToInt(UnityEngine.XR.XRSettings.eyeTextureWidth*UnityEngine.XR.XRSettings.renderViewportScale),
                h=Mathf.RoundToInt(UnityEngine.XR.XRSettings.eyeTextureHeight*UnityEngine.XR.XRSettings.renderViewportScale);
            float requested=VrSettings.Resolution.Value;
            if(w!=_eyeWidth||h!=_eyeHeight||requested!=_shownResolution) {
                _eyeWidth=w; _eyeHeight=h; _shownResolution=requested;
                bool pending=!Mathf.Approximately(requested,WorldStereoTrialPlugin.ActiveResolutionScale);
                _resolutionText.text=$"Current: {w} x {h} per eye"+(pending?$"  (Restart for {requested*100f:0}%)":"");
            }
        }
        private void Save() { VrSettings.Save(); if(_calibrationDirty) { AimPreviewPlugin.SaveCalibration(); _calibrationDirty=false; } }
        private void OnDestroy() { Save(); if(_headerMaterial!=null) Destroy(_headerMaterial); if(_radioSprite!=null) Destroy(_radioSprite); if(_radioSelectedSprite!=null) Destroy(_radioSelectedSprite); if(_radioTexture!=null) Destroy(_radioTexture); }
    }

    public sealed class VrMenuRow : scrMenuElement
    {
        internal Func<float>? GetValue; internal Action<float>? SetValue;
        internal Func<bool>? GetToggle; internal Action<bool>? SetToggle;
        internal Action? Action,Changed;
        internal float Min,Max,Step,Default;
        internal string Format="0";
        internal RectTransform? SliderBounds,SliderBar;
        internal Image? SliderImage,ToggleImage;
        internal Sprite[]? ToggleSprites;
        internal TextMeshProUGUI? ValueText;
        internal int[]? RadioValues;
        internal Image[]? RadioImages;
        internal Sprite? RadioEmpty,RadioFilled;
        internal TextMeshProUGUI[]? RadioTexts;
        private InputAction? _adjust;
        private float _repeatAt;
        private int _direction;
        public override void Start() { base.Start(); _adjust=scrGameControl.Instance.playerControls["Menu Adjust"]; Refresh(); }
        internal void Help(string text) {
            helpElementName=new lString{stringID="CulticVR."+name,fallbackString=controlMainText.text};
            helpElementDesc=new lString{stringID="CulticVR.Help."+name,fallbackString=text};
        }
        public override void Update()
        {
            base.Update();
            if(ignoreElement || GetValue==null || elementState!=ElementState.Selected || _adjust==null) return;
            float input=_adjust.ReadValue<float>();
            int dir=Mathf.Abs(input)>.2f?Math.Sign(input):0;
            if(dir==0) { _direction=0; return; }
            if(dir!=_direction||Time.unscaledTime>=_repeatAt) {
                Adjust(dir); _repeatAt=Time.unscaledTime+(dir!=_direction?.3f:.1f); _direction=dir;
            }
        }
        private void Adjust(int dir)
        {
            if(RadioValues!=null) {
                int i=Array.IndexOf(RadioValues,(int)GetValue!()); i=Mathf.Clamp(i+dir,0,RadioValues.Length-1);
                SetValue!(RadioValues[i]);
            } else SetValue!(Mathf.Clamp(GetValue!()+Step*dir,Min,Max));
            Refresh(); Changed?.Invoke(); systemMenu.playSound(systemMenu.soundBank[16]);
        }
        internal void Refresh()
        {
            if(GetToggle!=null && ToggleImage!=null) ToggleImage.overrideSprite=ToggleSprites![GetToggle()?1:0];
            if(GetValue!=null && RadioValues!=null) {
                for(int i=0;i<RadioValues.Length;i++) {
                    bool selected=RadioValues[i]==(int)GetValue();
                    RadioImages![i].sprite=selected?RadioFilled:RadioEmpty;
                    RadioImages[i].color=selected?systemMenu.menuElementSelectedColor:systemMenu.menuElementBaseColor;
                    RadioTexts![i].color=RadioImages[i].color;
                }
            } else if(GetValue!=null && SliderBar!=null) {
                float v=GetValue(); SliderBar.sizeDelta=new Vector2(SliderBounds!.rect.width*Mathf.InverseLerp(Min,Max,v),SliderBar.sizeDelta.y);
                ValueText!.text=Format=="0%"?(v*100f).ToString("0")+"%":v.ToString(Format);
            }
        }
        public override void Press()
        {
            if(GetToggle!=null) { SetToggle!(!GetToggle()); Refresh(); Changed?.Invoke(); }
            else Action?.Invoke();
            systemMenu.playSound(pressAudio);
        }
        public override void Select() { base.Select(); Refresh(); Paint(systemMenu.menuElementSelectedColor); }
        public override void Deselect() { base.Deselect(); Paint(systemMenu.menuElementBaseColor); }
        private void Paint(Color color) { if(ValueText!=null) ValueText.color=color; if(SliderImage!=null) SliderImage.color=color; }
        public override TextMeshProUGUI findActiveElementText()=>controlMainText;
    }
}
