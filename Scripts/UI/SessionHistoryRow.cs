using System;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Seans geçmişi satırı — havuzdan yeniden kullanılır; dil değişiminde GameObject yok edilmez.
/// </summary>
public class SessionHistoryRow : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private TextMeshProUGUI dateLabel;
    [SerializeField] private TextMeshProUGUI romLabel;
    [SerializeField] private TextMeshProUGUI metaLabel;
    [SerializeField] private TextMeshProUGUI detailLabel;
    [SerializeField] private Button detailButton;

    private SessionEntry _entry;
    private int _sessionNumber;
    private Action<SessionEntry> _onOpen;
    private bool _detailWired;
    private readonly StringBuilder _sb = new StringBuilder(160);
    private readonly StringBuilder _sb2 = new StringBuilder(96);

    private void Awake()
    {
        CacheRefs();
        WireDetailOnce();
    }

    public void Bind(SessionEntry entry, int sessionNumber, Action<SessionEntry> onOpen)
    {
        CacheRefs();
        WireDetailOnce();
        _entry = entry;
        _sessionNumber = sessionNumber;
        _onOpen = onOpen;
        RefreshLanguage();
    }

    public void RefreshLanguage()
    {
        if (_entry == null) return;
        CacheRefs();

        if (dateLabel != null)
            dateLabel.text = FormatSessionHeader(_sessionNumber, _entry.dateTime);
        if (romLabel != null)
            romLabel.text = BuildRomLine(_entry);
        if (metaLabel != null)
        {
            string meta = BuildMetaLine(_entry);
            metaLabel.text = meta.Length > 0 ? meta : Loc.T("menu.hist.tapHint");
        }
        if (detailLabel != null)
            detailLabel.text = Loc.T("menu.hist.detail");
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null || eventData.dragging) return;
        if (eventData.button != PointerEventData.InputButton.Left) return;
        Open();
    }

    private void Open()
    {
        if (_entry == null || _onOpen == null) return;
        _onOpen.Invoke(_entry);
    }

    private void CacheRefs()
    {
        if (dateLabel == null)
        {
            Transform t = transform.Find("Date");
            if (t != null) dateLabel = t.GetComponent<TextMeshProUGUI>();
        }
        if (romLabel == null)
        {
            Transform t = transform.Find("Rom");
            if (t != null) romLabel = t.GetComponent<TextMeshProUGUI>();
        }
        if (metaLabel == null)
        {
            Transform t = transform.Find("Meta");
            if (t != null) metaLabel = t.GetComponent<TextMeshProUGUI>();
        }
        if (detailButton == null)
        {
            Transform t = transform.Find("DetailBtn");
            if (t != null)
            {
                detailButton = t.GetComponent<Button>();
                if (detailLabel == null)
                    detailLabel = t.GetComponentInChildren<TextMeshProUGUI>(true);
            }
        }
    }

    private void WireDetailOnce()
    {
        if (_detailWired || detailButton == null) return;
        detailButton.onClick.AddListener(Open);
        _detailWired = true;
    }

    private string BuildRomLine(SessionEntry s)
    {
        _sb.Length = 0;
        bool showR = s.rightArmEnabled || s.rightMaxROM > 0f || s.rightCompletedReps > 0;
        bool showL = s.leftArmEnabled || s.leftMaxROM > 0f || s.leftCompletedReps > 0;
        if (!showR && !showL)
            showR = s.completedReps > 0 || s.maxROM > 0f;

        if (showR)
        {
            int rReps = (s.rightArmEnabled || s.rightCompletedReps > 0) ? s.rightCompletedReps : s.completedReps;
            _sb.Append(Loc.T("menu.hist.rightRom")).Append(' ')
              .Append(SessionHistoryFilter.EffectiveRightMax(s).ToString("F0")).Append('°')
              .Append(" · ").Append(rReps).Append('/').Append(s.targetReps);
        }
        if (showL)
        {
            if (_sb.Length > 0) _sb.Append("  ");
            _sb.Append(Loc.T("menu.hist.leftRom")).Append(' ')
              .Append(SessionHistoryFilter.EffectiveLeftMax(s).ToString("F0")).Append('°')
              .Append(" · ").Append(s.leftCompletedReps).Append('/').Append(s.targetReps);
        }
        return _sb.ToString();
    }

    private string BuildMetaLine(SessionEntry s)
    {
        _sb2.Length = 0;
        if (s.compensationEvents > 0)
            _sb2.Append(Loc.T("menu.hist.compensation")).Append(": ").Append(s.compensationEvents);
        if (s.peakStrain > 0f)
        {
            if (_sb2.Length > 0) _sb2.Append(" · ");
            _sb2.Append(Loc.T("menu.hist.strainPeak")).Append(" %")
              .Append((s.peakStrain * 100f).ToString("F0"));
        }
        if (s.movementScoreRight >= 0f || s.movementScoreLeft >= 0f)
        {
            if (_sb2.Length > 0) _sb2.Append(" · ");
            if (s.movementScoreRight >= 0f)
                _sb2.Append(Loc.T("menu.hist.dtw.right")).Append(' ')
                  .Append(Mathf.RoundToInt(s.movementScoreRight)).Append('%');
            if (s.movementScoreLeft >= 0f)
            {
                if (s.movementScoreRight >= 0f) _sb2.Append(" · ");
                _sb2.Append(Loc.T("menu.hist.dtw.left")).Append(' ')
                  .Append(Mathf.RoundToInt(s.movementScoreLeft)).Append('%');
            }
        }
        return _sb2.ToString();
    }

    private static string FormatSessionHeader(int sessionNumber, string dateTimeRaw)
    {
        string sessionPart = sessionNumber > 0
            ? Loc.Format("menu.hist.sessionN", sessionNumber)
            : Loc.T("menu.hist.session");

        if (SessionHistoryFilter.TryParseSessionDate(dateTimeRaw, out DateTime dt))
            return sessionPart + " · " + dt.ToString("HH:mm") + " · " + dt.ToString("dd/MM/yyyy");

        if (string.IsNullOrEmpty(dateTimeRaw))
            return sessionPart;
        return sessionPart + " · " + dateTimeRaw;
    }
}
