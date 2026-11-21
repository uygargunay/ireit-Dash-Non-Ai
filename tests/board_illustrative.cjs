// Invented test observations, unrelated to any uploaded organization.
const fs=require('fs'),vm=require('vm'),assert=require('node:assert/strict');
const context={};vm.createContext(context);vm.runInContext(fs.readFileSync('src/IreiMvp.Web/wwwroot/board-illustrative.js','utf8')+';this.demo=IreiIllustrative;',context);const api=context.demo;
const record={values:[['Existing homes',100],['Vacancy',.02],['Closing reserve headline',100000],['Restricted reserve',60000],['Cash minimum (proposed)',80000],['Headline average rent',700],['Market rent comparator',1500],['Valuation income proxy',100000],['Cap rate',.045],['New mortgage rate',.04],['Amortization',40],['First term',10],['Renewal rate 5%',.05],['Renewal rate 6%',.06],['Port Moody annual payment',400],['Adanac annual payment',200],['450 Pacific annual payment',250],['800 Commercial annual payment',350]].map(([label,value])=>({label,value,status:'Illustrative'}))};
const result=api.summary(record);assert.equal(result.occupied,98);assert.equal(result.unrestricted,40000);assert.equal(result.aboveFloor,20000);assert.ok(Math.abs(result.propertyValue-2222222.222)<1);assert.ok(Math.abs(result.priced[0].result.increment-149.32054)<1);
record.values.find(v=>v.label==='Vacancy').value=.1;assert.equal(api.summary(record).occupied,90);
record.values.find(v=>v.label==='New mortgage rate').value=.05;assert.ok(api.summary(record).priced[0].result.principal<result.priced[0].result.principal);
assert.equal(api.summary(null).occupied,null);assert.equal(api.summary(null).priced[0].result,null);assert.equal(api.mortgage(100,.04,10,10,.05),null);assert.equal(api.mortgage(120,0,40,10,0).balance,3600);
record.values.find(v=>v.label==='Restricted reserve').value=200000;assert.equal(api.summary(record).unrestricted,null);
record.values[0].status='Verified';assert.equal(api.summary(record).homes,null);
console.log('PASS illustrative arithmetic, input sensitivity, missing/invalid data, zero-rate loans and evidence separation');
