// Exercise the actual P2 headline renderer with invented observations.
const fs=require('fs'),vm=require('vm'),assert=require('node:assert/strict');
const context={URLSearchParams,location:{hash:'#P2',search:''},localStorage:{getItem:()=>''}};vm.createContext(context);
vm.runInContext(fs.readFileSync('src/IreiMvp.Web/wwwroot/board-illustrative.js','utf8'),context);
const source=fs.readFileSync('src/IreiMvp.Web/wwwroot/board.js','utf8');
vm.runInContext(source.slice(0,source.indexOf('  function barChart('))+`this.renderTiles=(record,registers={})=>{payload={illustrative:record,summary:{buildings:3,plannedHomes:150,plannedBy:'2030-2031'}};data={registers,currentMetrics:[]};page='P2';return portfolioMissionTiles().join('');};})();`,context);
const record={values:[['Headline average rent',700],['Market rent comparator',1500],['Existing homes',100],['Protected homes after 2028 low',40],['Protected homes after 2028 high',60],['Good condition',50],['Retrofit needed',30],['Near end of life',20],['People housed headline',200],['Children headline',40]].map(([label,value])=>({label,value,status:'Illustrative'}))};
const html=context.renderTiles(record);
const cards=[...html.matchAll(/<article[^>]*>([\s\S]*?)<\/article>/g)].map(m=>m[1]);assert.equal(cards.length,4);
for(const [i,label,value] of [[0,'Average rent','$700/month'],[1,'Homes protected','100'],[2,'Good condition','50'],[3,'People housed','About 200']]){
 assert.ok(cards[i].includes(label));assert.ok(cards[i].includes(`<strong>${value}</strong>`));assert.ok(cards[i].includes('illustrative-badge'));assert.ok(!cards[i].includes('No data'));
}
assert.ok(html.includes('40–60 after 2028'));assert.ok(html.includes('30 need retrofit; 20 near end of life'));assert.ok(html.includes('Including about 40 children'));
const absent=context.renderTiles(null);assert.equal((absent.match(/<strong>No data<\/strong>/g)||[]).length,4);assert.ok(!absent.includes('illustrative-badge'));
const partial={values:[record.values[0]]};const partialHtml=context.renderTiles(partial);assert.equal((partialHtml.match(/illustrative-badge/g)||[]).length,1);assert.equal((partialHtml.match(/<strong>No data<\/strong>/g)||[]).length,3);
const records=context.renderTiles(null,{'37_UNIT_RENT':[{}]});assert.ok(records.includes('<strong>Review records</strong>'));
record.values[0].value=0;assert.ok(context.renderTiles(record).includes('<strong>$0/month</strong>'));
console.log('PASS all four P2 headline cards, labels, captions, illustrative badges, missing/partial data, review records and numeric zero');
