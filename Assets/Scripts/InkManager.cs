﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Text.RegularExpressions;
using System.Text;
using Ink.Runtime;
using System.IO;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using System;
using UnityEngine.Networking;

[Serializable]
public class ResourceVariable
{
    public string InkVariableName;
    public string Prefix;
    public UILabel UILabel; 
}

public class InkManager : MonoBehaviour
{
    [SerializeField] bool _debugMode;


    [Header("Ink JSON Asset")]
    /// <summary>
    /// The JSON file containing the Ink story
    /// </summary>
    /// <remarks>
    /// This file should be placed in the "StreamingAssets" folder however you can't access files in the "StreamingAssets"
    /// folder in the Unity Editor, so you need to place the file in the "Resources" folder of the built game.
    /// You can also use the "ImportStoryFile" method to load the file from a different location.
    /// </remarks>
    [Tooltip("The JSON file containing the Ink story, placed in the \"Resources\" folder")]
    [SerializeField] private TextAsset _inkJsonAsset = null;

    public TextAsset InkJsonAsset { get => _inkJsonAsset; set => _inkJsonAsset = value; }

    public string _inkJsonAssetFileName;
 

    private Story _story;

    public Story Story { get => _story; set => _story = value; }
    bool _storyLoaded = false;

    bool _remoteFile = false;

    List<ChoiceButton> _allChoiceButtons = new List<ChoiceButton>();
    List<UILabel> _allUIText = new List<UILabel>();

    [Header("UI")]    
    [SerializeField] ChoiceButton _firstChoice;
    [SerializeField] ChoiceButton _secondChoice;
    [SerializeField] ChoiceButton _thirdChoice;

    [SerializeField] private TextMeshProUGUI displayNameText;

    [SerializeField] private GameObject dialoguePanel;

    [SerializeField] private Animator portraitAnimator;
     private Animator layoutAnimator;
    
    [SerializeField] UILabel _titleLabel;

    [Header("Audio")]
    [SerializeField] private DialogueAudioInfoSO defaultAudioInfo;
    [SerializeField] private DialogueAudioInfoSO[] audioInfos;
    [SerializeField] private bool makePredictable;
    private DialogueAudioInfoSO currentAudioInfo;
    private Dictionary<string, DialogueAudioInfoSO> audioInfoDictionary;
    private AudioSource audioSource;

    private const string SPEAKER_TAG = "speaker";
    private const string PORTRAIT_TAG = "portrait";
    private const string LAYOUT_TAG = "layout";
    private const string AUDIO_TAG = "audio";
    
    [SerializeField] TextMeshProUGUI _eventText;

    [Header("Resource Variables")]
    [SerializeField] ResourceVariable _topRightElement;
    [SerializeField] List<ResourceVariable> _allResourceVariables = new List<ResourceVariable>();

    [SerializeField] Volume _postProcessingVolume;
    [SerializeField] ChromaticAberration _aberration;

    // Effects-related variables
    bool _keepGlitching = false;
    float _minGlitch = .35f;
    float _maxGlitch = .55f;
    float _stepGlitch = .1f;
    
    string _defaultHexColor = "#FFCC00";




    // Stringbuilder used to manipulate and parse text to be displayed
    StringBuilder _sb;

    #region SINGLETON
    private static InkManager _instance;
    public static InkManager instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<InkManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject();
                    go.name = "[InkManager]";
                    _instance = go.AddComponent<InkManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(this.gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
        
        Initialize();
    }
    #endregion

    void Initialize()
    {
        _postProcessingVolume.profile.TryGet<ChromaticAberration>(out _aberration);
        _storyLoaded = false;

        _keepGlitching = false;
        StopAllCoroutines();

        // Initialize Lists of UI elements for quick reference
        _allChoiceButtons = new List<ChoiceButton>()
        {
            _firstChoice,
            _secondChoice,
            _thirdChoice
        };

        _allUIText = new List<UILabel>()
        {
            _titleLabel,
            _topRightElement.UILabel
        };
    }

    // Start is called before the first frame update
    IEnumerator Start()
    {
        if (_storyLoaded)
        {
            RemoveVariableObservers();
        }

        _storyLoaded = false;

        InitializeAudioInfoDictionary();

        dialoguePanel.SetActive(false);

        // get the layout animator
        layoutAnimator = dialoguePanel.GetComponent<Animator>();
        
        // Reset effects
        _keepGlitching = false;
        StopAllCoroutines();

        yield return StartCoroutine(ImportStoryFile());
    }

    IEnumerator ImportStoryFile()
{
    string path = System.IO.Path.Combine(Application.streamingAssetsPath, _inkJsonAssetFileName);
    string uri = path;

#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_MACOS
    uri = "file://" + path;
#endif

    DebugConsoleLog($"--- InkManager: Loading \"{uri}\"");

    using (UnityWebRequest www = UnityWebRequest.Get(uri))
    {
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.ProtocolError || www.result == UnityWebRequest.Result.ConnectionError)
        {
            Debug.Log(www.error);

            _firstChoice.TurnOff();
            _secondChoice.TurnOff();
            _thirdChoice.TurnOff();

            _titleLabel.UpdateLabel("ERROR");
            _eventText.text = "No \"story.json\" file found in the StreamingAssets folder!";
        }
        else
        {
            _inkJsonAsset = new TextAsset(www.downloadHandler.text);
            StartStory();
            _storyLoaded = true;
        }
    }
}



    /// <summary>
    /// Starts the Story, once loaded
    /// </summary>
    public void StartStory()
    {
        _story = new Story(_inkJsonAsset.text);

         // reset portrait, layout, and speaker
        displayNameText.text = "???";
        portraitAnimator.Play("default");
        layoutAnimator.Play("right");


        RemoveVariableObservers();
        AddVariableObservers();

        RefreshResourcesLabels();
        DebugConsoleLog("--- InkManager: new Story started!");
        ContinueStory();
    }


    private void AddVariableObservers()
    {
        _allResourceVariables.ForEach(v =>
        {
            _story.ObserveVariable(v.InkVariableName, (string varName, object newValue) =>
            {
                v.UILabel.UpdateLabel($"{v.Prefix}: {newValue}");
            });
        });

        _story.ObserveVariable(_topRightElement.InkVariableName, (string varName, object newValue) =>
        {
            _topRightElement.UILabel.UpdateLabel($"{_topRightElement.Prefix}: {newValue}");
        });
    }

    private void RemoveVariableObservers()
    {
        _allResourceVariables.ForEach(v =>
        {
            _story.RemoveVariableObserver(null, v.InkVariableName);
        });
    }

    /// <summary>
    /// Continues the Story, parsing through relevant content
    /// </summary>
    public void ContinueStory()
    {
        ClearUI();
        _sb = new StringBuilder();

        

        // Gets the next line
        while (_story.canContinue) _sb.Append(_story.Continue());

        // Parses possible commands
        DebugConsoleLog($"TO PARSE: {_sb}");
        _sb = new StringBuilder(ParseCommand(_sb.ToString()));

        

        // Removes whitespace
        _sb = new StringBuilder($"{_sb?.ToString().Trim()}");

        if (!string.IsNullOrEmpty($"{_sb}")) _eventText.text = $"{_sb}";

        // Display content until a choice or the end is reached
        if (!_story.canContinue)
        {
            dialoguePanel.SetActive(true);

            // handle tags

            DebugConsoleLog($"--- InkManager: {_story.currentTags.Count} tags available!");
            DebugConsoleLog($"--- InkManager: {_story.currentTags}");
            HandleTags(_story.currentTags);

            // If choices are available, display them
            // otherwise just turn off the choice buttons
            if (AreChoicesAvailable()) ShowChoices();
            else
            {
                _eventText.text = _story.currentText;
                _allChoiceButtons.ForEach(x => x.TurnOff());
            }
        }
        else ContinueStory();
    }

    /// <summary>
    /// Update the ChoiceButton objects with the relative text and ChoiceIndex
    /// </summary>
    public void ShowChoices()
    {
        if (_story.currentChoices.Count > 3 || _story.currentChoices.Count <= 0)
            Debug.LogError($"--- InkManager: wrong number of choices! ({_story.currentChoices.Count})");
        
        for (int i = 0; i < _story.currentChoices.Count; i++)
        {
            string choice = _story.currentChoices[i].text;
            string text = choice.Split(new string[] { "/" }, System.StringSplitOptions.None)[0];
            text = text.Trim();

            if (i == 0 && _story.currentChoices.Count == 1)
                _allChoiceButtons[1].UpdateChoiceText($"> {text}");
            else
                _allChoiceButtons[i].UpdateChoiceText($"> {text}");
        }

        // Updates the choice buttons with the correct text
        DebugConsoleLog($"--- InkManager: {_story.currentChoices.Count} choices available!");

        switch (_story.currentChoices.Count)
        {
            case 0:     
                break;

            case 1:
                _secondChoice.ChoiceIndex = 0;
                _secondChoice.TurnOn();
                break;
            
            case 2:
                _secondChoice.ChoiceIndex = 1;
                _firstChoice.ChoiceIndex = 0;

                _secondChoice.TurnOn();
                _firstChoice.TurnOn();
                break;

            case 3:
                _thirdChoice.ChoiceIndex = 0;
                _secondChoice.ChoiceIndex = 1;
                _firstChoice.ChoiceIndex = 2;

                _firstChoice.TurnOn();
                _secondChoice.TurnOn();
                _thirdChoice.TurnOn();
                break;
                
            default:
                Debug.LogError($"--- InkManager: wrong number of choices! ({_story.currentChoices.Count})");
                break;
        }

    }

    /// <summary>
    /// Show the outcome(s) when hovering a choice
    /// </summary>
    /// <param name="choiceIndex"></param>
    public void ShowOutcomes(int choiceIndex)
    {
        if (!AreChoicesAvailable()) return;
        
        string choice = _story.currentChoices[choiceIndex].text;
        
        // Checks whether choices cointain a specific symbol between whitespaces
        if (!choice.Contains(" / ")) return;

        // Collects the outcomes parsed from the choice text, after the symbol
        string outcomes = choice.Split(new string[] { "/" }, System.StringSplitOptions.None)[1];
        string[] parsedOutcomes = outcomes.Split(new string[] { " " }, System.StringSplitOptions.None);

        // Iterates through all the outcomes and updates UI accordingly
        foreach (string outcome in parsedOutcomes)
        {
            if (string.IsNullOrEmpty(outcome)) continue;

            bool negative = outcome.Contains("-") ? true : false;
            StringBuilder s = new StringBuilder();

            ResourceVariable resource = _allResourceVariables.Find(x => outcome.Contains(x.Prefix));
            
            if (resource != null)
            {
                s.Insert(0, resource.Prefix);
                if (negative) s.Append("--");
                else s.Append("++");

                resource.UILabel.Highlight();
                resource.UILabel.UpdateLabel($"{s}");
            }
            else
            {
                Debug.LogWarning($"--- InkManager: can't find a ResourceVariable for the outcome \"{outcome}\"");
            }
        }
    }

    /// <summary>
    /// Hides the outcomes from showing over the resources
    /// and resets the layout
    /// </summary>
    public void HideOutcomes()
    {
        _allResourceVariables.ForEach(x => x.UILabel.ResetColor());
        RefreshResourcesLabels();
    }

    /// <summary>
    /// Refreshes the resources' nad "remaining jumps" labels
    /// </summary>
    public void RefreshResourcesLabels()
    {
        _allResourceVariables.ForEach(x =>
        {
            x.UILabel.UpdateLabel($"{x.Prefix}: {_story.variablesState[x.InkVariableName]}");
        });

        _topRightElement.UILabel.UpdateLabel($"{_topRightElement.Prefix}: {_story.variablesState[_topRightElement.InkVariableName]}");
    }

    /// <summary>
    /// Turns off choice buttons, clears text, refreshes resources labels
    /// </summary>
    public void ClearUI()
    {
        _eventText.text = string.Empty;
        _allChoiceButtons.ForEach(x => x.TurnOff());
        RefreshResourcesLabels();

        SetCurrentAudioInfo(defaultAudioInfo.id);
    }

    /// <summary>
    /// Finds text preceded by ">>>" and parses it as a command
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    public string ParseCommand(string text)
    {
        Regex regex = new Regex(">>>(.*?)\n");
        foreach (Match match in regex.Matches(text))
        {
            text = text.Replace(match.Value, string.Empty);

            string wrappedText = match.Groups[1].Value;
            wrappedText = wrappedText.Replace(">>> ", string.Empty);

            string command = string.Empty;
            string parameter = string.Empty;

            if (wrappedText.Contains(":"))
            {
                command = wrappedText.Split(new string[] { ":" }, System.StringSplitOptions.None)[0];
                command = command.Trim();

                parameter = wrappedText.Split(new string[] { ":" }, System.StringSplitOptions.None)[1];
                parameter = parameter.Trim();
            }
            else
            {
                command = wrappedText;
                command = command.Trim();
            }

            // Elaborates the command and executes it
            switch (command)
            {
                // Chnges the Title
                case "TITLE":
                    _titleLabel.UpdateLabel(parameter.ToUpper());
                    break;

                // Changes the color of all UI elements
                case "COLOR":
                    if (parameter.Contains("RESET"))
                    {
                        ResetColorsToDefault();
                        break;
                    }

                    parameter = parameter.Insert(0, "#");
                    ColorUtility.TryParseHtmlString(parameter, out Color textColor);
                    UpdateUIColors(textColor);
                    break;

                // Plays a one shot
                case "PLAY":
                    AudioManager.instance.PlayOneShot(parameter);
                    break;

                case "PLAY LOOP":
                    AudioManager.instance.Play(parameter);
                    break;

                case "STOP":
                    if (parameter.Contains("ALL"))
                    {
                        AudioManager.instance.StopAllClips();
                        break;
                    }

                    AudioManager.instance.Stop(parameter);
                    break;

                case "GLITCH":
                    if (parameter.Contains("START"))
                    {
                        _keepGlitching = true;
                        StartCoroutine(GlitchEffect());
                    }
                    else if (parameter.Contains("STOP"))
                    {
                        _keepGlitching = false;
                        StopCoroutine(GlitchEffect());
                    }
                    break;
            }
        }
        return text;
    }

    bool AreChoicesAvailable() => _story.currentChoices.Count > 0;

    /// <summary>
    /// Play the selected selected
    /// </summary>
    /// <param name="choiceIndex"></param>
    public void SelectChoice(int choiceIndex)
    {
        if (!AreChoicesAvailable()) return;

        Choice choice = _story.currentChoices[choiceIndex];

        DebugConsoleLog($"--- InkManager: {choice.pathStringOnChoice}");
        DebugConsoleLog("--- InkManager: Chose an option!");

        _story.ChooseChoiceIndex(choiceIndex);
        HideOutcomes();
        
        ContinueStory();
    }

    /// <summary>
    /// Randomizes the intensity of the Color Aberration
    /// </summary>
    /// <returns></returns>
    IEnumerator GlitchEffect()
    {
        while (_keepGlitching)
        {
            yield return new WaitForSecondsRealtime(UnityEngine.Random.Range(.05f, .1f));
            _aberration.intensity.value = UnityEngine.Random.Range(0, (_maxGlitch - _minGlitch) / _stepGlitch * _stepGlitch + _minGlitch);
        }
        _aberration.intensity.value = 0f;
    }

    /// <summary>
    /// Updates the UI color scheme
    /// </summary>
    /// <param name="textColor"></param>
    private void UpdateUIColors(Color textColor)
    {
        _allChoiceButtons.ForEach(x => x.UpdateColors(textColor));
        _allChoiceButtons.ForEach(x => x.ResetColor());

        _allResourceVariables.ForEach(x => x.UILabel.UpdateColors(textColor));
        _allResourceVariables.ForEach(x => x.UILabel.ResetColor());

        _allUIText.ForEach(x => x.UpdateColors(textColor));
        _allUIText.ForEach(x => x.ResetColor());

        _eventText.color = new Color(textColor.r, textColor.g, textColor.b, textColor.a);
    }

    /// <summary>
    /// Reverts the color scheme to the default one
    /// </summary>
    private void ResetColorsToDefault()
    {
        ColorUtility.TryParseHtmlString(_defaultHexColor, out Color textColor);
        UpdateUIColors(textColor);
    }

    /// <summary>
    /// Prints Debug.Log messages only if "Debug Mode" is active
    /// </summary>
    /// <param name="message"></param>
    private void DebugConsoleLog(string message)
    {
        if (!_debugMode) return;
        Debug.Log(message);
    }

    // Update is called once per frame
    void Update()
    {
        // Hit R to reload the story file
        if (Input.GetKeyDown(KeyCode.R)) StartCoroutine(Start());
        if (Input.GetKeyDown(KeyCode.Escape)) Application.Quit();

        // If there is no story file loaded don't allow
        // other keypresses
        if (!_storyLoaded) return;


        switch (_story.currentChoices.Count)
        {
            case 1:
                if (Input.GetKeyDown(KeyCode.Alpha1)) SelectChoice(0);
                break;
            
            case 2:
                if (Input.GetKeyDown(KeyCode.Alpha1)) SelectChoice(0);
                if (Input.GetKeyDown(KeyCode.Alpha2)) SelectChoice(1);
                break;

             case 3:
                if (Input.GetKeyDown(KeyCode.Alpha1)) SelectChoice(0);
                if (Input.GetKeyDown(KeyCode.Alpha2)) SelectChoice(1);
                if (Input.GetKeyDown(KeyCode.Alpha3)) SelectChoice(2);
                break;
        }
    }


       private void HandleTags(List<string> currentTags)
    {
        // loop through each tag and handle it accordingly
        foreach (string tag in currentTags) 
        {
            // parse the tag
            string[] splitTag = tag.Split(':');
            if (splitTag.Length != 2) 
            {
                Debug.LogError("Tag could not be appropriately parsed: " + tag);
            }
            string tagKey = splitTag[0].Trim();
            string tagValue = splitTag[1].Trim();
            
            // handle the tag
            switch (tagKey) 
            {
                case SPEAKER_TAG:
                    displayNameText.text = tagValue;
                    break;
                case PORTRAIT_TAG:
                    portraitAnimator.Play(tagValue);
                    break;
                case LAYOUT_TAG:
                    layoutAnimator.Play(tagValue);
                    break;
                case AUDIO_TAG: 
                    SetCurrentAudioInfo(tagValue);
                    break;
                default:
                    Debug.LogWarning("Tag came in but is not currently being handled: " + tag);
                    break;
            }
        }
    }




        private void InitializeAudioInfoDictionary() 
    {
        audioInfoDictionary = new Dictionary<string, DialogueAudioInfoSO>();
        audioInfoDictionary.Add(defaultAudioInfo.id, defaultAudioInfo);
        foreach (DialogueAudioInfoSO audioInfo in audioInfos) 
        {
            audioInfoDictionary.Add(audioInfo.id, audioInfo);
        }
    }

        private void SetCurrentAudioInfo(string id) 
    {
        DialogueAudioInfoSO audioInfo = null;
        audioInfoDictionary.TryGetValue(id, out audioInfo);
        if (audioInfo != null) 
        {
            this.currentAudioInfo = audioInfo;
        }
        else 
        {
            Debug.LogWarning("Failed to find audio info for id: " + id);
        }
    }

}
