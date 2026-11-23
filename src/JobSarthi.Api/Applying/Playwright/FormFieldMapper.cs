using System.Text.Json;
using Microsoft.Playwright;

namespace JobSarthi.Api.Applying.Playwright;

/// <summary>Reads the fields of an application form out of the page, and performs planned actions on them.</summary>
public static class FormFieldMapper
{
    // Stamps every usable control with data-js-idx so it can be found again, and reads label, requiredness and options.
    public const string CollectScript = """
    () => {
      const clean = t => (t || '').replace(/\s+/g, ' ').replace(/\s*\*+\s*$/, '').replace(/\(required\)/i, '').trim();
      const visible = el => { const s = getComputedStyle(el), r = el.getBoundingClientRect();
        return s.visibility !== 'hidden' && s.display !== 'none' && (r.width > 0 || r.height > 0); };
      const labelOf = el => {
        if (el.labels && el.labels.length) return el.labels[0].innerText;
        const al = el.getAttribute('aria-label'); if (al) return al;
        const lb = el.getAttribute('aria-labelledby');
        if (lb) { const n = document.getElementById(lb.split(' ')[0]); if (n) return n.innerText; }
        // Nearest wrapper (up to 5 levels) that has a label of its own.
        let box = el.parentElement;
        for (let up = 0; box && box !== document.body && up < 5; up++, box = box.parentElement) {
          const l = box.querySelector('label, legend, [class*=label]');
          if (l && !l.querySelector('input, select, textarea') && clean(l.innerText)) return l.innerText;
          if (box.querySelectorAll('input:not([aria-hidden=true]):not(.requiredInput), select, textarea').length > 1) break;   // would borrow another question's label
        }
        return el.placeholder || el.name || '';
      };
      const groupLabel = el => {
        const fs = el.closest('fieldset'); const lg = fs && fs.querySelector('legend'); if (lg) return lg.innerText;
        const wrap = el.closest('.field, .application-field, .application-question, [class*=question], .form-group');
        const l = wrap && wrap.querySelector('label, legend, .question-label'); return l ? l.innerText : el.name;
      };
      // Which question a checkbox belongs to: shared name, else a fieldset / role=group, else the nearest
      // wrapper that holds several checkboxes and no other kind of input. Null = a lone yes/no box.
      let gid = 0;
      const checkGroupKey = el => {
        if (el.name && document.querySelectorAll('input[type=checkbox][name="' + CSS.escape(el.name) + '"]').length > 1) return 'n:' + el.name;
        let box = el.parentElement;
        for (let up = 0; box && box !== document.body && up < 5; up++, box = box.parentElement) {
          const inputs = [...box.querySelectorAll('input, select, textarea')].filter(i => !['hidden', 'submit', 'button'].includes((i.type || '').toLowerCase()));
          const boxes = inputs.filter(i => i.type === 'checkbox');
          if (boxes.length > 1 && boxes.length === inputs.length) {
            if (!box.dataset.jsGroup) box.dataset.jsGroup = 'g' + (gid++);
            return 'g:' + box.dataset.jsGroup;
          }
          if (inputs.length > boxes.length) break;   // reached a wrapper that mixes in other questions
          if (box.matches('fieldset, [role=group]')) break;
        }
        return null;
      };
      const out = [], groups = {}, checks = {}; let n = 0;
      for (const el of document.querySelectorAll('input, textarea, select')) {
        const type = (el.getAttribute('type') || 'text').toLowerCase();
        if (['hidden', 'submit', 'button', 'image', 'reset'].includes(type) || el.disabled) continue;
        if (type !== 'file' && !visible(el)) continue;
        // Helper inputs that custom dropdowns (react-select) put next to the real control: invisible, unlabelled, "required".
        if (!['file', 'checkbox', 'radio'].includes(type) && (el.getAttribute('aria-hidden') === 'true' || el.classList.contains('requiredInput')
            || (el.tabIndex === -1 && getComputedStyle(el).opacity === '0'))) continue;
        const idx = n++; el.setAttribute('data-js-idx', idx);
        const rawLabel = type === 'radio' ? groupLabel(el) : labelOf(el);
        const required = el.required || el.getAttribute('aria-required') === 'true' || /\*\s*$/.test((rawLabel || '').trim());
        const base = { idx, label: clean(rawLabel), name: el.name || '', id: el.id || '', autocomplete: el.getAttribute('autocomplete') || '',
                       required, inputType: el.getAttribute('role') === 'combobox' && el.tagName !== 'SELECT' ? 'combobox' : type, options: [] };
        if (type === 'radio') {
          const key = el.name || ('r' + idx);
          if (!groups[key]) { groups[key] = { ...base, type: 'Radio' }; out.push(groups[key]); }
          const ol = el.labels && el.labels.length ? el.labels[0].innerText : (el.value || '');
          groups[key].options.push({ idx, label: clean(ol) });
          continue;
        }
        if (type === 'checkbox') {
          // Boxes sharing a name, or sitting in one fieldset / role=group, answer one question ("select the countries ...").
          const key = checkGroupKey(el);
          const ol = clean(el.labels && el.labels.length ? el.labels[0].innerText : (el.getAttribute('aria-label') || el.value || ''));
          if (key) {
            if (!checks[key]) { checks[key] = { ...base, type: 'Checkbox', els: [] }; out.push(checks[key]); }
            checks[key].els.push(el); checks[key].options.push({ idx, label: ol });
            if (required) checks[key].required = true;
          } else out.push({ ...base, type: 'Checkbox' });
          continue;
        }
        if (type === 'file') { out.push({ ...base, type: 'File' }); continue; }
        if (el.tagName === 'SELECT') {
          base.options = [...el.options].map(o => clean(o.text)).filter(t => t && !/^(select|choose|--|please)/i.test(t)).map(t => ({ idx, label: t }));
          out.push({ ...base, type: 'Select' }); continue;
        }
        out.push({ ...base, type: el.tagName === 'TEXTAREA' ? 'Textarea' : 'Text' });
      }
      // The question of a checkbox group: the nearest text above the boxes that is not one of the box labels.
      const questionOf = (els, opts) => {
        const own = new Set(opts.map(o => o.label));
        const lb = els[0].closest('[aria-labelledby]'); 
        if (lb && !els.includes(lb)) { const t = document.getElementById(lb.getAttribute('aria-labelledby').split(' ')[0]); if (t && !own.has(clean(t.innerText))) return t.innerText; }
        let box = els[0].parentElement;
        while (box && !els.every(e => box.contains(e))) box = box.parentElement;
        for (let up = 0; box && up < 4; up++, box = box.parentElement) {
          for (const t of box.querySelectorAll('legend, label, h2, h3, h4, h5, p, [class*=label], [class*=question], [class*=title], span, div')) {
            if (t.querySelector('input, select, textarea')) continue;
            const txt = clean(t.innerText);
            if (txt && !own.has(txt) && txt.length > 3) return t.innerText;
          }
        }
        return els[0].name || '';
      };
      for (const f of out) {
        if (!f.els) continue;
        if (f.els.length > 1) {
          const q = questionOf(f.els, f.options);
          f.type = 'CheckboxGroup'; f.label = clean(q);
          f.required = f.required || /\*\s*$/.test((q || '').trim());
        } else { f.options = []; }   // a single box: plain yes/no checkbox, keeps its own label
        delete f.els;
      }
      return out;
    }
    """;

    public static async Task<List<FormField>> CollectAsync(IFrame frame)
    {
        var json = await frame.EvaluateAsync<JsonElement>(CollectScript);
        var fields = Parse(json);
        // Custom dropdowns ("Select...", role=combobox) only show their options when opened: open each, read, close.
        for (var i = 0; i < fields.Count; i++)
        {
            if (fields[i].InputType != "combobox") continue;
            var options = await ReadComboOptionsAsync(frame, fields[i].Idx);
            if (options.Count > 0)
                fields[i] = fields[i] with { Type = FieldType.Select, Options = options.Select(o => new FieldOption(fields[i].Idx, o)).ToList() };
        }
        return fields;
    }

    private const string VisibleOptionsScript = """
    () => [...document.querySelectorAll('[role=option]')]
      .filter(o => { const r = o.getBoundingClientRect(); return r.width > 0 || r.height > 0; })
      .map(o => (o.innerText || '').replace(/\s+/g, ' ').trim()).filter(t => t && !/^no (options|results)/i.test(t))
    """;

    /// <summary>The options a custom dropdown lists when opened. Empty for type-ahead boxes that only search as you type (e.g. School).</summary>
    public static async Task<List<string>> ReadComboOptionsAsync(IFrame frame, int idx)
    {
        var el = frame.Locator($"[data-js-idx='{idx}']");
        try
        {
            await el.ClickAsync(new() { Timeout = 4000 });
            List<string> options = new();
            for (var attempt = 0; attempt < 4 && options.Count == 0; attempt++)
            {
                await Task.Delay(250);
                options = (await frame.EvaluateAsync<string[]>(VisibleOptionsScript)).Distinct().Take(400).ToList();
            }
            await el.PressAsync("Escape", new() { Timeout = 2000 });
            return options;
        }
        catch (PlaywrightException) { return new(); }
    }

    /// <summary>Picks an option in a custom dropdown: open it, type to filter, click the option whose text matches.</summary>
    private static async Task<bool> PickComboOptionAsync(IFrame frame, ILocator el, string option, bool exact)
    {
        await el.ClickAsync(new() { Timeout = 4000 });
        await el.FillAsync(option, new() { Timeout = 4000 });
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(300);   // type-ahead lists load from the server
            var opts = frame.Locator("[role=option]");
            var count = await opts.CountAsync();
            var texts = new List<string>();
            for (var i = 0; i < count; i++) texts.Add(((await opts.Nth(i).InnerTextAsync()) ?? "").Trim());
            var hit = texts.FindIndex(t => t.Equals(option, StringComparison.OrdinalIgnoreCase));
            if (hit < 0 && !exact) hit = texts.FindIndex(t => t.Contains(option, StringComparison.OrdinalIgnoreCase));
            if (hit >= 0) { await opts.Nth(hit).ClickAsync(new() { Timeout = 4000 }); return true; }
        }
        await el.PressAsync("Escape");
        return false;
    }

    /// <summary>Turns the collect script's JSON into fields. Shared with the MCP draft flow.</summary>
    public static List<FormField> Parse(JsonElement json)
    {
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

    /// <summary>Returned when a dropdown has no choice matching the answer: the run asks you again instead of failing.</summary>
    public const string NoMatch = "No matching choice on the form";

    private static async Task<bool> IsComboAsync(ILocator el) =>
        await el.EvaluateAsync<bool>("e => e.tagName !== 'SELECT' && e.getAttribute('role') === 'combobox'");

    /// <summary>Performs one planned action. Returns an error message, or null when it worked.</summary>
    public static async Task<string?> ExecuteAsync(IFrame frame, PlannedAction action)
    {
        var el = frame.Locator($"[data-js-idx='{action.Idx}']");
        try
        {
            switch (action)
            {
                case FillAction a when await IsComboAsync(el):
                    // A type-ahead box (School, City): type, then take the matching suggestion if one shows up.
                    if (!await PickComboOptionAsync(frame, el, a.Value, exact: false)) return NoMatch;
                    break;
                case FillAction a: await el.FillAsync(a.Value, new() { Timeout = 8000 }); break;
                case SelectAction a when await IsComboAsync(el):
                    if (!await PickComboOptionAsync(frame, el, a.Option, exact: true)) return NoMatch;
                    break;
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
