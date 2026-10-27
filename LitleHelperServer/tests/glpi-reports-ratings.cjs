const fs = require('fs'), vm = require('vm'), assert = require('assert'), path = require('path');
const reportSrc = fs.readFileSync(path.join(__dirname, '../wwwroot/glpi-reports.js'), 'utf8');

// Mock DOM environment
const elements = new Map();
function element(sel) {
  if (!elements.has(sel)) {
    elements.set(sel, {
      innerHTML: '',
      textContent: '',
      value: '',
      hidden: false,
      style: {},
      dataset: {},
      handlers: {},
      addEventListener(e, fn) { this.handlers[e] = fn; },
      querySelectorAll(subSel) {
        if (subSel === '[data-score]') {
          const matches = [];
          const regex = /<button[^>]*data-score="(\d+)"[^>]*>/g;
          let m;
          while ((m = regex.exec(this.innerHTML)) !== null) {
            const scoreVal = m[1];
            matches.push({
              dataset: { score: scoreVal },
              addEventListener(ev, f) {
                this.handler = f;
              },
              click() { if (this.handler) this.handler(); }
            });
          }
          return matches;
        }
        return [];
      },
      querySelector(subSel) {
        if (subSel === '#report-score-clear') {
          return this.handlers['click'] ? this : null;
        }
        return null;
      },
      after() {},
      insertAdjacentHTML() {},
      scrollIntoView() {}
    });
  }
  return elements.get(sel);
}

let apiCalls = [];
let mockReportData = {
  filter: { from: '2026-10-01', to: '2026-10-27', basis: 'created', comparison: 'none' },
  choices: { branches: [], technicians: [], requesters: [] },
  summary: { total: 10, closed: 8, meanSeconds: 3600, medianSeconds: 3000, p90Seconds: 7000, meanScore: 4.5, rated: 6, effortSeconds: 5000, executionSamples: 6, effortSamples: 4, missingRequester: 0, ambiguousResponsible: 0 },
  tickets: [
    { id: 1, title: 'T1', status: 6, currentScore: 5, requesters: [], assignees: [] },
    { id: 2, title: 'T2', status: 6, currentScore: 5, requesters: [], assignees: [] },
    { id: 3, title: 'T3', status: 6, currentScore: 4, requesters: [], assignees: [] },
    { id: 4, title: 'T4', status: 6, currentScore: 3, requesters: [], assignees: [] },
    { id: 5, title: 'T5', status: 6, currentScore: 2, requesters: [], assignees: [] },
    { id: 6, title: 'T6', status: 6, currentScore: 1, requesters: [], assignees: [] },
    { id: 7, title: 'T7', status: 5, currentScore: null, requesters: [], assignees: [] }
  ],
  ratings: [
    { score: 5, count: 2 },
    { score: 4, count: 1 },
    { score: 3, count: 1 },
    { score: 2, count: 1 },
    { score: 1, count: 1 }
  ],
  timeline: [],
  reaction: [],
  reactionMissing: 0,
  currentOpen: 1
};

const sandbox = {
  $: element,
  api: async (url, method, body) => {
    apiCalls.push({ url, method, body });
    if (url.startsWith('/reports/glpi/context')) return { timeZone: 'Asia/Almaty' };
    if (url.startsWith('/reports/glpi?')) {
      const u = new URL('http://local' + url);
      const score = u.searchParams.get('score');
      return {
        ...mockReportData,
        filter: { ...mockReportData.filter, score: score ? Number(score) : null }
      };
    }
    return {};
  },
  can: () => true,
  escape: s => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;'),
  toast: () => {},
  generation: 1,
  page: 'reports',
  token: 'mock-token',
  reportDate: () => '',
  reportTime: () => '',
  reportScore: () => '',
  reportStatus: () => '',
  reportHints: {},
  sortedReportRows: (items) => items || [],
  table: (h, r) => `<table>${r}</table>`,
  reportColumnHeader: h => h,
  bindReportColumnHints: () => {},
  bindReportSorting: () => {},
  closeReportHints: () => {},
  reportComparisonView: () => '',
  surveyReportHtml: () => '',
  document: {
    addEventListener: () => {},
    createElement: () => ({ textContent: '', addEventListener: () => {}, style: {} }),
    querySelectorAll: (sel) => element('#content').querySelectorAll(sel)
  },
  window: {
    addEventListener: () => {}
  },
  modal: () => {},
  closeModal: () => {},
  URLSearchParams
};

vm.createContext(sandbox);

// Run the script
vm.runInContext(reportSrc, sandbox);

async function test() {
  // Test selectReportScore
  await vm.runInContext('selectReportScore(5)', sandbox);
  const filters1 = vm.runInContext('glpiReportFilters', sandbox);
  const tab1 = vm.runInContext('glpiReportTab', sandbox);
  const page1 = vm.runInContext('glpiReportPage', sandbox);
  assert.strictEqual(filters1.score, '5', 'Score should be set to 5');
  assert.strictEqual(tab1, 'tickets', 'Tab should switch to tickets');
  assert.strictEqual(page1, 0, 'Page should be 0');

  // Verify renderGlpiReports rendered stars chart with buttons
  const starsChartHtml = element('#report-stars-chart').innerHTML;
  assert(starsChartHtml.includes('data-score="5"'), 'Stars chart should have data-score="5"');
  assert(starsChartHtml.includes('selected'), '5-star button should have selected class');
  assert(starsChartHtml.includes('2</b>'), '5-star button should have count 2');

  // Verify selection indicator is rendered in panel
  const contentHtml = element('#content').innerHTML;
  assert(contentHtml.includes('report-score-selection'), 'Selection banner should be rendered');
  assert(contentHtml.includes('Выбрана оценка: 5 ★'), 'Selection text should include rating');
  assert(contentHtml.includes('report-score-clear'), 'Clear button should be rendered');

  // Clicking same score toggles it off
  await vm.runInContext('selectReportScore(5)', sandbox);
  const filters2 = vm.runInContext('glpiReportFilters', sandbox);
  assert.strictEqual(filters2.score, undefined, 'Clicking same score should toggle it off');

  // Clear button test
  await vm.runInContext('selectReportScore(4)', sandbox);
  const filters3 = vm.runInContext('glpiReportFilters', sandbox);
  assert.strictEqual(filters3.score, '4', 'Score should be 4');
  await vm.runInContext('selectReportScore(null)', sandbox);
  const filters4 = vm.runInContext('glpiReportFilters', sandbox);
  assert.strictEqual(filters4.score, undefined, 'selectReportScore(null) should clear score');

  // Changing period clears score and reaction
  await vm.runInContext('selectReportScore(5)', sandbox);
  await vm.runInContext('selectReportReaction("1")', sandbox);
  let filtersAfterScore = vm.runInContext('glpiReportFilters', sandbox);
  assert.strictEqual(filtersAfterScore.score, '5', 'Score should be 5 before period change');
  assert.strictEqual(filtersAfterScore.reaction, '1', 'Reaction should be 1 before period change');
  vm.runInContext('reportPeriod("year")', sandbox);
  let filtersAfterPeriod = vm.runInContext('glpiReportFilters', sandbox);
  assert.strictEqual(filtersAfterPeriod.score, undefined, 'Changing period to year must reset score filter');
  assert.strictEqual(filtersAfterPeriod.reaction, undefined, 'Changing period must reset reaction filter');
  assert.strictEqual(filtersAfterPeriod.period, 'year', 'Period should be set to year');

  // Test selectReportReaction toggle and clear
  await vm.runInContext('selectReportReaction("2")', sandbox);
  let filtersReaction = vm.runInContext('glpiReportFilters', sandbox);
  assert.strictEqual(filtersReaction.reaction, '2', 'Reaction should be set to 2');
  await vm.runInContext('selectReportReaction("2")', sandbox);
  filtersReaction = vm.runInContext('glpiReportFilters', sandbox);
  assert.strictEqual(filtersReaction.reaction, undefined, 'Re-clicking reaction 2 should toggle it off');

  await vm.runInContext('selectReportReaction("3")', sandbox);
  filtersReaction = vm.runInContext('glpiReportFilters', sandbox);
  assert.strictEqual(filtersReaction.reaction, '3', 'Reaction should be set to 3');
  await vm.runInContext('selectReportReaction(null)', sandbox);
  filtersReaction = vm.runInContext('glpiReportFilters', sandbox);
  assert.strictEqual(filtersReaction.reaction, undefined, 'selectReportReaction(null) should clear reaction');

  console.log('PASS GLPI Reports: interactive rating buttons, reaction toggle/clear, selection banner, score toggle and clear, period reset');
}

test().catch(err => {
  console.error(err);
  process.exit(1);
});
