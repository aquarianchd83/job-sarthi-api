using System.Text.Json;
using Microsoft.Playwright;

namespace JobSarthi.Api.Applying.Playwright;

/// <summary>Reads the fields of an application form out of the page, and performs planned actions on them.</summary>
public static class FormFieldMapper
{
    // Stamps every usable control with data-js-idx so it can be found again, and reads label, requiredness and options.
    private const string CollectScript = """
    () => {
      const clean = t => (t || '').replace(/\s+/g, ' ').replace(/\s*\*+\s*$/, '').replace(/\(required\)/i, '').trim();
      const visible = el => { const s = getComputedStyle(el), r = el.getBoundingClientRect();
        return s.visibility !== 'hidden' && s.display !== 'none' && (r.width > 0 || r.height > 0); };
      const labelOf = el => {
        if (el.labels && el.labels.length) return el.labels[0].innerText;
        const al = el.getAttribute('aria-label'); if (al) return al;
        const lb = el.getAttribute('aria-labelledby');
        if (lb) { const n = document.getElementById(lb.split(' ')[0]); if (n) return n.innerText; }
        const wrap = el.closest('.field, .application-field, .application-question, [class*=question], fieldset, li, .form-group, div');
        const l = wrap && wrap.querySelector('label, legend');
        return l ? l.innerText : (el.placeholder || el.name || '');
      };
      const groupLabel = el => {
        const fs = el.closest('fieldset'); const lg = fs && fs.querySelector('legend'); if (lg) return lg.innerText;
        const wrap = el.closest('.field, .application-field, .application-question, [class*=question], .form-group');
        const l = wrap && wrap.querySelector('label, legend, .question-label'); return l ? l.innerText : el.name;
      };
      const out = [], groups = {}; let n = 0;
      for (const el of document.querySelectorAll('input, textarea, select')) {
        const type = (el.getAttribute('type') || 'text').toLowerCase();
        if (['hidden', 'submit', 'button', 'image', 'reset'].includes(type) || el.disabled) continue;
        if (type !== 'file' && !visible(el)) continue;
        const idx = n++; el.setAttribute('data-js-idx', idx);
        const rawLabel = type === 'radio' ? groupLabel(el) : labelOf(el);
        const required = el.required || el.getAttribute('aria-required') === 'true' || /\*\s*$/.test((rawLabel || '').trim());
        const base = { idx, label: clean(rawLabel), name: el.name || '', id: el.id || '', autocomplete: el.getAttribute('autocomplete') || '',
                       required, inputType: type, options: [] };
        if (type === 'radio') {
          const key = el.name || ('r' + idx);
          if (!groups[key]) { groups[key] = { ...base, type: 'Radio' }; out.push(groups[key]); }
          const ol = el.labels && el.labels.length ? el.labels[0].innerText : (el.value || '');
          groups[key].options.push({ idx, label: clean(ol) });
          continue;
        }
        if (type === 'checkbox') { out.push({ ...base, type: 'Checkbox' }); continue; }
        if (type === 'file') { out.push({ ...base, type: 'File' }); continue; }
        if (el.tagName === 'SELECT') {
          base.options = [...el.options].map(o => clean(o.text)).filter(t => t && !/^(select|choose|--|please)/i.test(t)).map(t => ({ idx, label: t }));
          out.push({ ...base, type: 'Select' }); continue;
        }
        out.push({ ...base, type: el.tagName === 'TEXTAREA' ? 'Textarea' : 'Text' });
      }
      return out;
    }
    """;

    public static async Task<List<FormField>> CollectAsync(IFrame frame)
    {
        var json = await frame.EvaluateAsync<JsonElement>(CollectScript);
        var list = new List<FormField>();
        foreach (var e in json.EnumerateArray())
        {
            var opts = e.GetProperty("options").EnumerateArray()
                .Select(o => new FieldOption(o.GetProperty("idx").GetInt32(), o.GetProperty("label").GetString() ?? "")).ToList();
            list.Add(new FormField(
                e.GetProperty("idx").GetInt32(), Enum.Parse<FieldType>(e.GetProperty("type").GetString()!),
                e.GetProperty("label").GetString() ?? "", e.GetProperty("name").GetString() ?? "", e.GetProperty("id").GetString() ?? "",
                e.GetProperty("autocomplete").GetString() ?? "", e.GetProperty("required").GetBoolean(),
                e.GetProperty("inputType").GetString() ?? "text", opts));
        }
        return list;
    }

    /// <summary>Performs one planned action. Returns an error message, or null when it worked.</summary>
    public static async Task<string?> ExecuteAsync(IFrame frame, PlannedAction action)
    {
        var el = frame.Locator($"[data-js-idx='{action.Idx}']");
        try
        {
            switch (action)
            {
                case FillAction a: await el.FillAsync(a.Value, new() { Timeout = 8000 }); break;
                case SelectAction a: await el.SelectOptionAsync(new SelectOptionValue { Label = a.Option }, new() { Timeout = 8000 }); break;
                case UploadAction a: await el.SetInputFilesAsync(a.Path, new() { Timeout = 15000 }); break;
                case ClickAction a:
                    try { await el.CheckAsync(new() { Timeout = 4000 }); }
                    catch (PlaywrightException) { await el.ClickAsync(new() { Force = true, Timeout = 4000 }); } // styled radios hide the real input
                    break;
            }
            return null;
        }
        catch (PlaywrightException ex)
        {
            return ex.Message.Split('\n')[0];
        }
    }
}
