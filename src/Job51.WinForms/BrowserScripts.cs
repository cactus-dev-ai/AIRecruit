using System.Text.Json;

namespace Job51.WinForms;

internal static class BrowserScripts
{
    public static string LoadPublishedTalentJobsViaPageApi => """
        (() => {
          let webpackRequire = window.__job51WebpackRequire;
          if (!webpackRequire && Array.isArray(window.webpackJsonp)) {
            const moduleId = '__job51_jobs_' + Date.now();
            const modules = {};
            modules[moduleId] = (module, exports, require) => {
              window.__job51WebpackRequire = require;
            };
            window.webpackJsonp.push([[moduleId], modules, [[moduleId]]]);
            webpackRequire = window.__job51WebpackRequire;
          }
          if (!webpackRequire)
            return JSON.stringify({ok:false, message:'未取得51job接口模块加载器'});
          let api = null;
          try { api = webpackRequire('5377')?.a; } catch (_) {}
          if (typeof api !== 'function')
            return JSON.stringify({ok:false, message:'未取得51job签名接口模块5377'});
          try {
            Promise.resolve(api('getTalentSearchCombineJobList', {
              key_word:'',
              page_size:20,
              return_co_name:'1',
              need_offline_active_type:'1',
              return_ai_keyword:'1'
            })).catch(error => { window.__job51JobListError = String(error?.message || error); });
            return JSON.stringify({ok:true, message:'已请求完整已发布职位列表'});
          } catch (error) {
            return JSON.stringify({ok:false, message:String(error?.message || error)});
          }
        })();
        """;

    public static string StartTalentSearchViaPageApi(
        TalentJobApiData job,
        int pageIndex = 1,
        string requestId = "",
        bool excludeViewedCandidates = true)
    {
        var requestJson = JsonSerializer.Serialize(new
        {
            jobid = job.JobId,
            keyword = job.AiKeyword,
            keyword_type = "6",
            action_type = "",
            check_search = 1,
            page_index = Math.Max(1, pageIndex),
            page_size = 50,
            expect_job_area = "",
            is_expect_job_area = "",
            job_name = "",
            only_cur_job_name = "",
            work_func = "",
            only_cur_work_func = "",
            only_cur_industry = "",
            degree_from = "",
            degree_to = "",
            is_graduate_type = "",
            is985 = "",
            is211 = "",
            is_double_first = "",
            is_full_time = "",
            is_overseas_edu = "",
            age_from = "",
            age_to = "",
            work_year_from = "",
            work_year_to = "",
            is_overseas_work = "",
            area = "",
            expect_func = "",
            expect_salary_from = "",
            expect_salary_to = "",
            company_name = "",
            is_last_company = "",
            graduate_school = "",
            major = "",
            change_work_count_from = "",
            change_work_count_to = "",
            recent_work_time_from = "100",
            recent_work_time_to = "",
            avg_work_time_from = "",
            avg_work_time_to = "",
            graduate_from = "",
            graduate_to = "",
            for_language = "",
            for_language_ability = "",
            job_status = "",
            job_seeking_status = "1",
            resume_active_date = "1",
            chk_select_job_resume = "0",
            is_has_video = "",
            active_search = "1",
            chk_less_job_hopping = "1",
            chk_upgrading = "",
            chk_unread_resume = excludeViewedCandidates ? "1" : "",
            chk_hi_chat = "",
            chk_ctmid_hi_deliver = "",
            chk_select_dssm = "0",
            chk_select_open = "",
            chk_has_pic = "",
            extract_type = "",
            selected_talent_new = "",
            sort_type = "2",
            requestid = requestId,
            job_work_func = job.FunctionTypeCode,
            industry_new = "186,187",
            expect_industry_new = "",
            exposure_resumeids = ""
        });
        return $$"""
            (() => {
              let webpackRequire = window.__job51WebpackRequire;
              if (!webpackRequire && Array.isArray(window.webpackJsonp)) {
                const moduleId = '__job51_api_' + Date.now();
                const modules = {};
                modules[moduleId] = (module, exports, require) => {
                  window.__job51WebpackRequire = require;
                };
                window.webpackJsonp.push([[moduleId], modules, [[moduleId]]]);
                webpackRequire = window.__job51WebpackRequire;
              }
              if (!webpackRequire)
                return JSON.stringify({ok:false, message:'未取得51job接口模块加载器'});
              let api = null;
              try { api = webpackRequire('5377')?.a; } catch (_) {}
              if (typeof api !== 'function')
                return JSON.stringify({ok:false, message:'未取得51job签名接口模块5377'});
              const request = {{requestJson}};
              window.__job51ApiSearchError = '';
              try {
                Promise.resolve(api('talentHuntResumeList', request))
                  .then(result => { window.__job51ApiSearchResult = result; })
                  .catch(error => { window.__job51ApiSearchError = String(error?.message || error); });
                return JSON.stringify({
                  ok:true,
                  message:'已通过51job签名接口模块提交人才搜索',
                  selected_position:{{JsonSerializer.Serialize(job.JobName)}},
                  job_id:{{JsonSerializer.Serialize(job.JobId)}}
                });
              } catch (error) {
                return JSON.stringify({ok:false, message:String(error?.message || error)});
              }
            })();
            """;
    }

    public static string GetSearchPageContext => """
        (() => {
          const visible = e => !!(e && (e.offsetWidth || e.offsetHeight || e.getClientRects().length));
          const text = e => (e?.innerText || e?.textContent || e?.value || e?.getAttribute?.('title') || '')
            .replace(/\s+/g, ' ').trim();
          const validPosition = value => value &&
            !/^(期望工作地|请选择职位|选择职位|不限职位|我的职位|请输入职位名)$/.test(value) &&
            !/^(工作年限|年龄|性别|期望学历|求职状态|期望行业|期望职能|期望月薪)$/.test(value);

          let selectedPosition = '';
          let detectionSource = '';

          const legacy = document.querySelector('.talent_search_head_left');
          if (visible(legacy) && validPosition(text(legacy))) {
            selectedPosition = text(legacy);
            detectionSource = 'legacy';
          }

          if (!selectedPosition) {
            const selectedOptions = [...document.querySelectorAll(
              '[aria-selected="true"],.is-selected,.selected,.active,[class*="selected"]')]
              .filter(visible)
              .filter(e => {
                const popup = e.closest('[role="listbox"],.el-select-dropdown,.el-popper,.dropdown-menu,[class*="dropdown"]');
                return popup && (/我的职位/.test(text(popup)) || popup.querySelector('input[placeholder*="职位"]'));
              });
            const option = selectedOptions.find(e => validPosition(text(e)));
            if (option) {
              selectedPosition = (option.innerText || option.textContent || '').split(/\r?\n/)[0].trim();
              detectionSource = 'selected-option';
            }
          }

          if (!selectedPosition) {
            const candidates = [...document.querySelectorAll('button,[role="button"],[role="combobox"],input,[title]')]
              .filter(visible);
            const workplace = candidates.find(e => text(e) === '期望工作地' || text(e).startsWith('期望工作地'));
            if (workplace) {
              const area = workplace.getBoundingClientRect();
              const positionControl = candidates
                .filter(e => e !== workplace)
                .map(e => ({e, label:text(e), rect:e.getBoundingClientRect()}))
                .filter(x => validPosition(x.label) && x.rect.width >= 60 &&
                  x.rect.left >= area.right - 8 && x.rect.left <= area.right + 520 &&
                  Math.abs(x.rect.top - area.top) <= 18)
                .sort((a, b) => a.rect.left - b.rect.left || b.rect.width - a.rect.width)[0];
              if (positionControl) {
                selectedPosition = positionControl.label;
                detectionSource = 'same-row-control';
              }
            }
          }

          if (!selectedPosition) {
            const semantic = [...document.querySelectorAll(
              '[class*="position"],[class*="job-select"],[class*="jobSelect"],[data-field*="job"],[aria-label*="职位"]')]
              .filter(visible)
              .map(e => text(e))
              .find(validPosition);
            if (semantic) {
              selectedPosition = semantic;
              detectionSource = 'semantic-control';
            }
          }

          const keyword = document.querySelector('.talent_search_keywords input');
          return JSON.stringify({
            selected_position: selectedPosition,
            keyword_expression: (keyword?.value || keyword?.placeholder || '').trim(),
            detection_source: detectionSource
          });
        })();
        """;

    public static string SelectFirstAvailablePosition => """
        (() => {
          const visible = e => !!(e && (e.offsetWidth || e.offsetHeight || e.getClientRects().length));
          const normalize = value => (value || '').replace(/\s+/g, ' ').trim();
          const controls = [...document.querySelectorAll('button,[role="button"],[role="combobox"],input')]
            .filter(visible);
          let dropdowns = [...document.querySelectorAll('[role="listbox"],.el-select-dropdown,.el-popper,[class*="dropdown"]')]
            .filter(visible)
            .filter(e => /我的职位/.test(normalize(e.innerText || e.textContent)) || e.querySelector('input[placeholder*="职位"]'));
          let dropdown = dropdowns.at(-1);
          const workplace = controls.find(e => normalize(e.innerText || e.textContent || e.value) === '期望工作地');
          let positionControl = null;
          if (workplace) {
            const area = workplace.getBoundingClientRect();
            positionControl = controls
              .filter(e => e !== workplace)
              .map(e => ({e, label:normalize(e.innerText || e.textContent || e.value), rect:e.getBoundingClientRect()}))
              .filter(x => x.rect.width >= 80 && x.rect.left >= area.right - 8 && x.rect.left <= area.right + 560 &&
                Math.abs(x.rect.top - area.top) <= 20 && !/^(搜索|期望工作地)$/.test(x.label))
              .sort((a, b) => a.rect.left - b.rect.left)[0]?.e || null;
          }
          positionControl ||= controls.find(e => /请选择职位|选择职位|我的职位/.test(
            normalize(e.innerText || e.textContent || e.value || e.placeholder)));
          if (!dropdown)
          {
            if (!positionControl)
              return JSON.stringify({ok:false, message:'未找到职位下拉框'});
            positionControl.click();
            return JSON.stringify({ok:true, message:'已打开职位下拉框，等待职位列表加载'});
          }
          const options = [...dropdown.querySelectorAll('[role="option"],li,a,button,div')]
            .filter(visible)
            .map(e => ({e, label:normalize(e.innerText || e.textContent)}))
            .filter(x => x.label && x.label.length >= 4 && x.label.length <= 80 &&
              !/^(我的职位|请输入职位名|搜索)$/.test(x.label));
          const option = options.find(x => /嘉兴/.test(x.label)) || options[0];
          if (!option)
            return JSON.stringify({ok:true, message:'职位列表正在加载'});
          (option.e.closest('[role="option"],li,a,button') || option.e).click();
          return JSON.stringify({ok:true, message:'已选择第一条可用职位：' + option.label});
        })();
        """;

    public static string RemoveFilterTag(string prefix)
    {
        var value = JsonSerializer.Serialize(prefix);
        return $$"""
            (() => {
              const visible = e => !!(e && (e.offsetWidth || e.offsetHeight || e.getClientRects().length));
              const normalize = value => (value || '').replace(/\s+/g, '').trim();
              const wanted = normalize({{value}});
              const tag = [...document.querySelectorAll('.el-tag,[class*="tag"]')]
                .filter(visible)
                .find(e => normalize(e.innerText || e.textContent).startsWith(wanted + ':') ||
                  normalize(e.innerText || e.textContent).startsWith(wanted + '：'));
              if (!tag) return JSON.stringify({ok:false, message:'未发现筛选标签：' + {{value}}});
              const close = tag.querySelector('.el-tag__close,[class*="close"]');
              if (!close) return JSON.stringify({ok:false, message:'筛选标签没有关闭按钮：' + {{value}}});
              close.click();
              return JSON.stringify({ok:true, message:'已取消筛选：' + {{value}}});
            })();
            """;
    }

    public static string ClickText(string text, bool contains = false)
    {
        var value = JsonSerializer.Serialize(text);
        var compare = contains ? "label.includes(wanted)" : "label === wanted";
        return $$"""
            (() => {
              const visible = e => !!(e.offsetWidth || e.offsetHeight || e.getClientRects().length);
              const normalize = value => (value || '').replace(/\s+/g, '').trim();
              const wanted = normalize({{value}});
              const nodes = [...document.querySelectorAll('button,a,[role="button"],[role="option"],[role="menuitem"],label,li,p,span,div')]
                .filter(visible);
              const matches = nodes.filter(e => {
                const label = normalize(e.innerText || e.textContent);
                return {{compare}};
              });
              const target = matches.find(e => ['BUTTON','A','LABEL','LI'].includes(e.tagName) || e.getAttribute('role')) || matches[0];
              if (!target) return JSON.stringify({ok:false, message:'未找到：' + {{value}}});
              target.click();
              return JSON.stringify({ok:true, message:'已点击：' + {{value}}});
            })();
            """;
    }

    public static string OpenFilter(string controlName)
    {
        var value = JsonSerializer.Serialize(controlName);
        return $$"""
            (() => {
              const visible = e => !!(e.offsetWidth || e.offsetHeight || e.getClientRects().length);
              const normalize = value => (value || '').replace(/\s+/g, '').trim();
              const wanted = normalize({{value}});
              const button = [...document.querySelectorAll('button,[role="button"]')]
                .filter(visible)
                .find(e => normalize(e.innerText || e.textContent).startsWith(wanted));
              if (button) {
                button.click();
                return JSON.stringify({ok:true, message:'已打开筛选：' + {{value}}});
              }
              const input = [...document.querySelectorAll('input')]
                .filter(visible)
                .find(e => normalize(e.placeholder).includes(wanted));
              if (input) {
                (input.parentElement || input).click();
                input.click();
                return JSON.stringify({ok:true, message:'已打开筛选：' + {{value}}});
              }
              return JSON.stringify({ok:false, message:'未找到筛选：' + {{value}}});
            })();
            """;
    }

    public static string SelectFilterOption(string option)
    {
        var value = JsonSerializer.Serialize(option);
        return $$"""
            (() => {
              const visible = e => !!(e && (e.offsetWidth || e.offsetHeight || e.getClientRects().length));
              const normalize = value => (value || '').replace(/[‐‑‒–—−]/g, '-').replace(/\s+/g, '').trim();
              const wanted = normalize({{value}});
              const popups = [...document.querySelectorAll(
                '[role="listbox"],.el-popper,.el-select-dropdown,[class*="dropdown"],[class*="popover"]')]
                .filter(visible);
              const roots = popups.length ? popups : [document.body];
              const matches = roots.flatMap(root => [...root.querySelectorAll(
                '[role="option"],[role="menuitem"],li,button,a,label,p,span,div')])
                .filter(visible)
                .map(e => ({e, label:normalize(e.innerText || e.textContent), rect:e.getBoundingClientRect()}))
                .filter(x => x.label === wanted)
                .sort((a, b) => {
                  const ai = ['BUTTON','A','LI','LABEL'].includes(a.e.tagName) || a.e.getAttribute('role') ? 0 : 1;
                  const bi = ['BUTTON','A','LI','LABEL'].includes(b.e.tagName) || b.e.getAttribute('role') ? 0 : 1;
                  return ai - bi || (a.rect.width * a.rect.height) - (b.rect.width * b.rect.height);
                });
              if (!matches.length)
                return JSON.stringify({ok:false, message:'筛选弹层中未找到选项：' + {{value}}});
              const matched = matches[0].e;
              const target = matched.closest('[role="option"],[role="menuitem"],li,button,a,label') || matched;
              target.dispatchEvent(new MouseEvent('mousedown', {bubbles:true, cancelable:true, view:window}));
              target.dispatchEvent(new MouseEvent('mouseup', {bubbles:true, cancelable:true, view:window}));
              target.click();
              return JSON.stringify({ok:true, message:'已选择筛选选项：' + {{value}}});
            })();
            """;
    }

    public static string SetDialogSearch(string value)
    {
        var serializedValue = JsonSerializer.Serialize(value);
        return $$"""
            (() => {
              const visible = e => !!(e.offsetWidth || e.offsetHeight || e.getClientRects().length);
              const dialogs = [...document.querySelectorAll('[role="dialog"],.el-dialog,.el-popover')].filter(visible);
              const input = dialogs.flatMap(d => [...d.querySelectorAll('input')])
                .find(e => visible(e) && !e.disabled && /请输入|搜索/.test(e.placeholder || ''));
              if (!input) return JSON.stringify({ok:false, message:'筛选弹层中未找到搜索框'});
              const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
              setter.call(input, {{serializedValue}});
              input.dispatchEvent(new Event('input', {bubbles:true}));
              input.dispatchEvent(new Event('change', {bubbles:true}));
              return JSON.stringify({ok:true, message:'筛选弹层搜索：' + {{serializedValue}}});
            })();
            """;
    }

    public static string ClickDialogSearchResult(string value)
    {
        var serializedValue = JsonSerializer.Serialize(value);
        return $$"""
            (() => {
              const visible = e => !!(e && (e.offsetWidth || e.offsetHeight || e.getClientRects().length));
              const normalize = value => (value || '').replace(/\s+/g, '').trim();
              const wanted = normalize({{serializedValue}});
              const inputs = [...document.querySelectorAll('input')]
                .filter(visible)
                .filter(e => /请输入|搜索/.test(e.placeholder || '') && normalize(e.value) === wanted);
              const input = inputs.at(-1);
              if (!input) return JSON.stringify({ok:false, message:'未找到行业搜索框或搜索内容尚未写入'});

              const inputRect = input.getBoundingClientRect();
              const matches = [...document.querySelectorAll('[role="option"],li,button,a,div,span')]
                .filter(visible)
                .map(e => ({e, label:normalize(e.innerText || e.textContent), rect:e.getBoundingClientRect()}))
                .filter(x => x.label === wanted && x.rect.top >= inputRect.bottom - 4 &&
                  x.rect.top <= inputRect.bottom + 240 && x.rect.width >= 40)
                .sort((a, b) => a.rect.top - b.rect.top || a.rect.left - b.rect.left || a.rect.width - b.rect.width);
              if (!matches.length)
                return JSON.stringify({ok:false, message:'行业搜索结果中未找到：' + {{serializedValue}}});

              const matched = matches[0].e;
              const target = matched.closest('[role="option"],li,button,a') || matched;
              target.dispatchEvent(new MouseEvent('mousedown', {bubbles:true, cancelable:true, view:window}));
              target.dispatchEvent(new MouseEvent('mouseup', {bubbles:true, cancelable:true, view:window}));
              target.click();
              return JSON.stringify({ok:true, message:'已点击行业搜索结果：' + {{serializedValue}}});
            })();
            """;
    }

    public static string SelectDialogOptionFallback(string value)
    {
        var serializedValue = JsonSerializer.Serialize(value);
        return $$"""
            (() => {
              const visible = e => !!(e && (e.offsetWidth || e.offsetHeight || e.getClientRects().length));
              const normalize = value => (value || '').replace(/\s+/g, '').trim();
              const wanted = normalize({{serializedValue}});
              const dialogs = [...document.querySelectorAll('[role="dialog"],.el-dialog,[class*="dialog"],[class*="modal"]')]
                .filter(visible)
                .filter(e => /选择行业/.test(e.innerText || e.textContent || ''));
              const dialog = dialogs.at(-1);
              if (!dialog) return JSON.stringify({ok:false, message:'未找到“选择行业”窗口'});

              const selectedTag = [...dialog.querySelectorAll('.el-tag,[class*="tag"],[class*="selected"]')]
                .filter(visible)
                .find(e => normalize(e.innerText || e.textContent).startsWith(wanted));
              if (selectedTag)
                return JSON.stringify({ok:true, already_selected:true, message:'行业已在已选列表：' + {{serializedValue}}});

              const input = [...dialog.querySelectorAll('input')].filter(visible)[0];
              const minimumTop = input ? input.getBoundingClientRect().bottom + 55 : dialog.getBoundingClientRect().top + 120;
              const matches = [...dialog.querySelectorAll('[role="option"],li,button,a,div,span')]
                .filter(visible)
                .map(e => ({e, label:normalize(e.innerText || e.textContent), rect:e.getBoundingClientRect()}))
                .filter(x => x.rect.top >= minimumTop &&
                  (x.label === wanted || (x.label.startsWith(wanted) && x.label.length <= wanted.length + 2)))
                .sort((a, b) => a.rect.top - b.rect.top || a.rect.left - b.rect.left || a.rect.width - b.rect.width);
              if (!matches.length)
                return JSON.stringify({ok:false, message:'行业分类列表中未找到：' + {{serializedValue}}});

              const matched = matches[0].e;
              const target = matched.closest('[role="option"],li,button,a') || matched;
              target.click();
              return JSON.stringify({ok:true, already_selected:false, message:'已从行业分类列表选择：' + {{serializedValue}}});
            })();
            """;
    }

    public static string DiscoverCandidateTargets => """
        (() => {
          const visible = e => !!(e.offsetWidth || e.offsetHeight || e.getClientRects().length);
          const clean = value => (value || '').trim().replace(/\s+/g, ' ');
          const hash = value => {
            let h = 2166136261;
            for (let i = 0; i < value.length; i++) {
              h ^= value.charCodeAt(i);
              h = Math.imul(h, 16777619);
            }
            return (h >>> 0).toString(16).padStart(8, '0');
          };
          const idFromUrl = url => {
            const valid = value => value && /^[a-z0-9_-]{6,}$/i.test(value) &&
              !/^(resume|detail|talent|candidate|user|search|management)$/i.test(value);
            const queryMatch = (url || '').match(/[?&](?:resumeId|candidateId|talentId|userId|id)=([a-z0-9_-]{6,})/i);
            if (valid(queryMatch?.[1])) return queryMatch[1];
            const pathMatch = (url || '').match(/(?:resume|candidate|talent|user)(?:id)?[\/=:_-]+([a-z0-9_-]{6,})/i);
            return valid(pathMatch?.[1]) ? pathMatch[1] : '';
          };
          const idFromNode = node => {
            const nodes = [node, ...node.querySelectorAll('*')].slice(0, 250);
            for (const element of nodes) {
              for (const attribute of element.attributes || []) {
                if (!/(resume|candidate|talent|user).*(id)|(^|-)id$/i.test(attribute.name)) continue;
                if (/^[a-z0-9_-]{6,}$/i.test(attribute.value)) return attribute.value;
              }
            }
            return '';
          };
          const idFromVue = node => {
            const roots = [];
            let ancestor = node;
            for (let i = 0; i < 5 && ancestor; i++, ancestor = ancestor.parentElement) {
              if (ancestor.__vue__) roots.push(ancestor.__vue__);
            }
            for (const element of [node, ...node.querySelectorAll('*')].slice(0, 250)) {
              if (element.__vue__) roots.push(element.__vue__);
            }
            const queue = roots.map(value => ({value, depth:0}));
            const visited = new Set();
            let inspected = 0;
            while (queue.length && inspected++ < 400) {
              const {value, depth} = queue.shift();
              if (!value || typeof value !== 'object' || visited.has(value)) continue;
              visited.add(value);
              for (const key of Object.keys(value)) {
                let child;
                try { child = value[key]; } catch { continue; }
                if (/^(resumeId|resume_id|candidateId|candidate_id|talentId|talent_id|userId)$/i.test(key)) {
                  const candidate = String(child ?? '');
                  if (/^[a-z0-9_-]{6,}$/i.test(candidate)) return candidate;
                }
                if (depth < 2 && !/^\$(parent|root|children|el)$/.test(key)) {
                  if (child && typeof child === 'object') queue.push({value:child, depth:depth + 1});
                }
              }
            }
            return '';
          };
          const targets = [];
          const seen = new Set();
          const add = (node, url, label) => {
            const cardText = clean(node.innerText || node.textContent).slice(0, 2200);
            const talentId = idFromUrl(url) || idFromNode(node) || idFromVue(node) || ('card_' + hash(cardText));
            if (!talentId || seen.has(talentId)) return;
            seen.add(talentId);
            const domIndex = targets.length;
            node.setAttribute('data-job51-target', String(domIndex));
            targets.push({talent_id:talentId, url:url || '', label:clean(label || cardText).slice(0,160), dom_index:domIndex});
          };

          for (const anchor of document.querySelectorAll('a[href]')) {
            const href = anchor.getAttribute('href') || '';
            if (!href || href.startsWith('javascript:')) continue;
            let absolute = '';
            try { absolute = new URL(href, location.href).href; } catch { continue; }
            if (!absolute.includes('51job.com') || !/(resume|candidate|talent|person|detail)/i.test(absolute) ||
                /(search|management|manage|login|message|chat)/i.test(absolute)) continue;
            add(anchor, absolute, anchor.innerText || anchor.textContent);
          }

          const intentNodes = [...document.querySelectorAll('body *')]
            .filter(visible)
            .filter(e => /^求职意向[:：]/.test(clean(e.innerText || e.textContent)) && clean(e.innerText || e.textContent).length < 260);
          for (const intent of intentNodes) {
            let card = intent;
            for (let depth = 0; depth < 8 && card.parentElement; depth++) {
              const parentText = clean(card.parentElement.innerText || card.parentElement.textContent);
              if (parentText.length > 180 && parentText.length < 5000 &&
                  /\d+岁/.test(parentText) && /(本科|硕士|大专|博士|高中)/.test(parentText)) {
                card = card.parentElement;
                break;
              }
              card = card.parentElement;
            }
            const anchor = card.querySelector('a[href]');
            let url = '';
            if (anchor) { try { url = new URL(anchor.getAttribute('href'), location.href).href; } catch {} }
            add(card, url, intent.innerText || intent.textContent);
          }
          return JSON.stringify(targets);
        })();
        """;

    public static string ClickCandidateTarget(int domIndex) => $$"""
        (() => {
          const target = document.querySelector('[data-job51-target="{{domIndex}}"]');
          if (!target) return JSON.stringify({ok:false, message:'候选人卡片已失效，请重新执行搜索'});
          const clickable = target.matches('a,button,[role="button"]') ? target :
            target.querySelector('a,[class*="name"],[class*="title"],[class*="intent"],[role="button"]') || target;
          clickable.click();
          return JSON.stringify({ok:true, message:'已打开候选人详情'});
        })();
        """;

    public static string ClickMoreFilters => """
        (() => {
          const visible = e => !!(e && (e.offsetWidth || e.offsetHeight || e.getClientRects().length));
          const normalize = value => (value || '').replace(/\s+/g, '').trim();
          const otherFiltersVisible = [...document.querySelectorAll('button,a,[role="button"],span,div')]
            .filter(visible)
            .some(e => normalize(e.innerText || e.textContent) === '其他筛选');
          if (otherFiltersVisible)
            return JSON.stringify({ok:true, already_expanded:true, message:'“更多筛选”已经展开'});

          const matches = [...document.querySelectorAll('button,a,[role="button"],span,div,img[alt]')]
            .filter(visible)
            .map(e => ({e, label:normalize(e.innerText || e.textContent || e.getAttribute('alt')), rect:e.getBoundingClientRect()}))
            .filter(x => x.label.includes('更多筛选') && x.label.length <= 12)
            .sort((a, b) => a.rect.top - b.rect.top || b.rect.left - a.rect.left || a.rect.width - b.rect.width);
          if (!matches.length) return JSON.stringify({ok:false, message:'未找到顶部“更多筛选”'});
          const matched = matches[0].e;
          const target = matched.closest('button,a,[role="button"]') || matched.parentElement || matched;
          target.click();
          return JSON.stringify({ok:true, message:'已点击顶部“更多筛选”'});
        })();
        """;

    public static string ClickApplicantInformation => """
        (() => {
          const visible = e => !!(e && (e.offsetWidth || e.offsetHeight || e.getClientRects().length));
          const normalize = value => (value || '').replace(/\s+/g, '').trim();
          const containers = [...document.querySelectorAll(
            '[role="dialog"],.el-dialog,.el-drawer,[class*="drawer"],[class*="detail"]')].filter(visible);
          const roots = containers.length ? containers : [document.body];
          for (const root of roots) {
            const matches = [...root.querySelectorAll('button,a,[role="button"],[role="tab"],li,span,div')]
              .filter(visible)
              .filter(e => normalize(e.innerText || e.textContent) === '应聘者信息');
            const matched = matches.find(e => ['BUTTON','A','LI'].includes(e.tagName) || e.getAttribute('role')) || matches[0];
            if (!matched) continue;
            const target = matched.closest('button,a,[role="button"],[role="tab"],li') || matched;
            target.click();
            return JSON.stringify({ok:true, message:'已点击“应聘者信息”并载入详情'});
          }
          return JSON.stringify({ok:false, message:'候选人详情中未找到“应聘者信息”'});
        })();
        """;

    public static string VerifyCandidateDetail => """
        (() => {
          const visible = e => !!(e && (e.offsetWidth || e.offsetHeight || e.getClientRects().length));
          const bodyText = (document.body?.innerText || document.body?.textContent || '').replace(/\s+/g, ' ');
          if (/\/resume\/detail/i.test(location.pathname) && bodyText.length > 200 &&
              /应聘者信息|求职意向|工作经历|教育经历/.test(bodyText))
            return JSON.stringify({ok:true, message:'已确认候选人详情页及详情内容'});
          const panels = [...document.querySelectorAll(
            '[role="dialog"],.el-dialog,.el-drawer,[class*="drawer"],[class*="detail"]')].filter(visible);
          const panel = panels.find(e => {
            const value = (e.innerText || e.textContent || '').replace(/\s+/g, ' ');
            return value.length > 120 && /应聘者信息|求职意向|工作经历|教育经历/.test(value);
          });
          return JSON.stringify(panel
            ? {ok:true, message:'已确认候选人详情区域'}
            : {ok:false, message:'当前仍是人才列表页，未载入候选人详情'});
        })();
        """;

    public static string CloseCandidateOverlay => """
        (() => {
          const visible = e => !!(e.offsetWidth || e.offsetHeight || e.getClientRects().length);
          const overlays = [...document.querySelectorAll('[role="dialog"],.el-dialog,.el-drawer')].filter(visible);
          const close = overlays.flatMap(e => [...e.querySelectorAll('button,[aria-label="Close"],.el-dialog__close,.el-drawer__close-btn')])
            .find(visible);
          if (!close) return JSON.stringify({ok:false, message:'未找到候选人详情关闭按钮'});
          close.click();
          return JSON.stringify({ok:true, message:'已关闭候选人详情'});
        })();
        """;

    public static string ScrollToLoadMoreCandidates => """
        (() => {
          const visible = e => !!(e && (e.offsetWidth || e.offsetHeight || e.getClientRects().length));
          const candidates = [document.scrollingElement, ...document.querySelectorAll('main,section,div')]
            .filter(e => e && visible(e) && e.scrollHeight > e.clientHeight + 120)
            .map(e => ({e, overflow:e.scrollHeight - e.clientHeight,
              hasTalent:/求职意向|工作经历|岁/.test((e.innerText || e.textContent || '').slice(0,12000))}))
            .sort((a, b) => Number(b.hasTalent) - Number(a.hasTalent) || b.overflow - a.overflow);
          const target = candidates[0]?.e || document.scrollingElement;
          if (!target) return JSON.stringify({ok:false, message:'未找到可滚动的人才列表'});
          target.scrollTop = target.scrollHeight;
          window.scrollTo(0, document.documentElement.scrollHeight);
          target.dispatchEvent(new Event('scroll', {bubbles:true}));
          window.dispatchEvent(new Event('scroll'));
          return JSON.stringify({ok:true, message:'已拖动人才列表到底部，等待追加50条'});
        })();
        """;

    public static string ExtractCandidatePage => """
        (() => {
          const text = (document.body?.innerText || '').trim();
          const visible = e => !!(e.offsetWidth || e.offsetHeight || e.getClientRects().length);
          const clean = value => (value || '').trim().replace(/\s+/g, ' ');
          const validId = value => value && /^[a-z0-9_-]{6,}$/i.test(value) &&
            !/^(resume|detail|talent|candidate|user|search|management)$/i.test(value);
          const queryId = (location.href.match(/[?&](?:resumeId|candidateId|talentId|userId|id)=([a-z0-9_-]{6,})/i) || [])[1] || '';
          const pathId = (location.href.match(/(?:resume|candidate|talent|user)(?:id)?[\/=:_-]+([a-z0-9_-]{6,})/i) || [])[1] || '';
          const idFromUrl = validId(queryId) ? queryId : (validId(pathId) ? pathId : '');
          let talentId = idFromUrl;
          if (!talentId) {
            const idElement = [...document.querySelectorAll('[data-resume-id],[data-candidate-id],[data-talent-id],[data-user-id]')].find(visible);
            talentId = idElement?.getAttribute('data-resume-id') || idElement?.getAttribute('data-candidate-id') ||
              idElement?.getAttribute('data-talent-id') || idElement?.getAttribute('data-user-id') || '';
          }
          const nameSelectors = [
            '[role="dialog"] [class*="name"]', '.el-drawer [class*="name"]',
            '[class*="resume"] [class*="name"]', '[class*="candidate"] [class*="name"]',
            '[class*="user"] [class*="name"]', 'h1', 'h2'
          ];
          let candidateName = null;
          for (const selector of nameSelectors) {
            const element = [...document.querySelectorAll(selector)].find(visible);
            const value = clean(element?.innerText || element?.textContent);
            if (value && value.length <= 30) { candidateName = value; break; }
          }
          const lines = text.split(/\n+/).map(clean).filter(Boolean);
          const headline = lines.find(line => /\d+岁.*\d+年.*(本科|硕士|大专|博士|高中)/.test(line)) ||
            lines.find(line => /(求职意向|期望职位|应聘职位)/.test(line)) || null;
          const attachmentLinks = [...document.querySelectorAll('a[href]')]
            .filter(a => /(附件|简历|作品集|下载)/.test(clean(a.innerText || a.textContent)))
            .map(a => { try { return new URL(a.getAttribute('href'), location.href).href; } catch { return ''; } })
            .filter(Boolean);
          return JSON.stringify({
            talent_id: talentId,
            source_route: location.pathname,
            page_title: document.title,
            candidate_name: candidateName,
            headline,
            visible_text: text,
            attachment_links: [...new Set(attachmentLinks)]
          });
        })();
        """;
}
