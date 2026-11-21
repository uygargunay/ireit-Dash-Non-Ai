'use strict';
// Presentation-only scenarios. Never fed into source financial metrics or approvals.
const IreiIllustrative = (() => {
  const value = (record, label) => {
    const rows = (record?.values || []).filter(v => v.label === label && v.status === 'Illustrative');
    return rows.length === 1 && Number.isFinite(rows[0].value) ? rows[0].value : null;
  };
  function mortgage(annualPayment, rate, years, term, renewalRate) {
    if (![annualPayment, rate, years, term, renewalRate].every(Number.isFinite) || annualPayment <= 0 ||
        rate < 0 || rate > 1 || renewalRate < 0 || renewalRate > 1 || term <= 0 || years <= term) return null;
    const r = rate / 12, months = years * 12, paid = term * 12;
    const principal = r === 0 ? annualPayment / 12 * months : annualPayment / 12 * (1 - Math.pow(1 + r, -months)) / r;
    const balance = r === 0 ? principal - annualPayment / 12 * paid : principal * Math.pow(1 + r, paid) - annualPayment / 12 * (Math.pow(1 + r, paid) - 1) / r;
    const q = renewalRate / 12, remaining = months - paid;
    const renewedAnnual = q === 0 ? balance / remaining * 12 : balance * q / (1 - Math.pow(1 + q, -remaining)) * 12;
    return { principal, balance, renewedAnnual, increment: renewedAnnual - annualPayment };
  }
  function summary(record) {
    const v = label => value(record, label);
    const homes = v('Existing homes'), vacancy = v('Vacancy'), rent = v('Headline average rent'), market = v('Market rent comparator');
    const total = v('Closing reserve headline'), restricted = v('Restricted reserve'), floor = v('Cash minimum (proposed)');
    const payments = ['Port Moody annual payment', 'Adanac annual payment', '450 Pacific annual payment', '800 Commercial annual payment'].map(v);
    const annual = payments.every(p => p != null && p >= 0) ? payments.reduce((a, b) => a + b, 0) : null;
    const rate = v('New mortgage rate'), years = v('Amortization'), term = v('First term');
    const priced = [v('Renewal rate 5%'), v('Renewal rate 6%')].map(q => ({ rate: q, result: mortgage(annual, rate, years, term, q) }));
    return { homes, occupied: homes != null && homes >= 0 && vacancy != null && vacancy >= 0 && vacancy <= 1 ? Math.round(homes * (1 - vacancy)) : null,
      belowMarket: rent != null && market > 0 ? 1 - rent / market : null,
      unrestricted: total != null && restricted != null && restricted >= 0 && total >= restricted ? total - restricted : null,
      aboveFloor: total != null && floor != null ? total - floor : null,
      propertyValue: v('Valuation income proxy') != null && v('Cap rate') > 0 ? v('Valuation income proxy') / v('Cap rate') : null,
      annual, priced };
  }
  return Object.freeze({ value, mortgage, summary });
})();
