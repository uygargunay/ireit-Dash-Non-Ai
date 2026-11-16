"""V2 board upload, source reconciliation and recalculation regression tests."""
import base64
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import urllib.request
import urllib.error
import uuid
import zipfile
from xml.etree import ElementTree as ET
from xml.sax.saxutils import escape
import smoke_evidence as portal

ROOT = Path(__file__).resolve().parents[1]
portal.BASE = 'http://127.0.0.1:5098'
BASE = portal.BASE
passed = 0

def check(value, message):
    global passed
    assert value, message
    passed += 1
    print('PASS:', message, flush=True)

def metric(result, code, full=False):
    return next(m for m in result['view']['fullOperationMetrics' if full else 'currentMetrics'] if m['code'] == code)['value']

def upload(client, files, name='Synthetic board test', status=200, year=''):
    boundary = 'board-' + uuid.uuid4().hex
    data = bytearray()
    for key, value in [('organizationName', name), ('currentYear', year)]:
        data.extend(f'--{boundary}\r\nContent-Disposition: form-data; name="{key}"\r\n\r\n{value}\r\n'.encode())
    for filename, content in files:
        data.extend(f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{filename}"\r\nContent-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet\r\n\r\n'.encode())
        data.extend(content)
        data.extend(b'\r\n')
    data.extend(f'--{boundary}--\r\n'.encode())
    request = urllib.request.Request(BASE+'/api/submissions/board/assessments', data=bytes(data), headers={
        'Content-Type': 'multipart/form-data; boundary='+boundary, 'X-CSRF-Token': client.csrf})
    try:
        response = client.opener.open(request, timeout=90)
    except urllib.error.HTTPError as error:
        response = error
    body = response.read()
    assert response.status == status, (response.status, body[:1500])
    return json.loads(body) if body else {}

def synthetic_source():
    sheets = {
        'Portfolio source': {'B1':'10 YEAR CASH-FLOW PROJECTION 2025-2028','B6':'Fiscal Year','C6':'2025-2026','D6':'2026-2027','E6':'2027-2028',
            'B7':'Rental Revenue - ALL PROPERTY - NO NEW PROJECTS','C7':500,'D7':600,'E7':650,
            'B8':'Rental Revenue - NEW PROJECTS','C8':0,'D8':200,'E8':400,
            'B9':'BC Housing Subsidy Revenue Budgeted','C9':100,'D9':200,'E9':300,
            'B32':'TOTAL OTHER INCOME','C32':50,'D32':50,'E32':50,
            'B33':'TOTAL REVENUE','C33':650,'D33':1050,'E33':1400,
            'B76':'TOTAL SOCIETY OPERATING EXPENSES','C76':100,'D76':150,'E76':200,
            'B77':'TOTAL BUILDING OPERATING EXPENSES','C77':200,'D77':300,'E77':400,
            'B78':'TOTAL MORTGAGE Principal payments','C78':100,'D78':200,'E78':300,
            'B79':'TOTAL MORTGAGE Interest payments','C79':100,'D79':200,'E79':600,
            'B87':'RESERVE FUNDS','C87':1000,'D87':900,'E87':1100,
            'B88':'NET RESERVE FUNDS','C88':1100,'D88':1100,'E88':1000},
        'Homes ALL': {'B6':'Fiscal Year','C6':'2025-2026','D6':'2026-2027','E6':'2027-2028','B7':'UNITS','C7':20,'D7':30,'E7':30},
        'Existing home': {'A1':'Example Court','B3':'Fiscal Year','C3':'2025-2026','D3':'2026-2027','E3':'2027-2028','B16':'TOTAL REVENUE','C16':650,'D16':700,'E16':750,'B87':'TOTAL OPERATING EXPENSE','C87':200,'D87':220,'E87':240,'B90':'Mortgage','C90':-100,'D90':-100,'E90':20},
        'New project': {'A1':'Example Development','B3':'Fiscal Year','C3':'2025-2026','D3':'2026-2027','E3':'2027-2028','B16':'TOTAL REVENUE','C16':0,'D16':0,'E16':400,'A15':'BCH SUBSIDY PLACEHOLDER','B15':'Subsidy','C15':0,'D15':0,'E15':300,'B87':'TOTAL OPERATING EXPENSE','C87':0,'D87':0,'E87':160,'B90':'Mortgage','C90':0,'D90':0,'E90':300}
    }
    buf = io.BytesIO()
    with zipfile.ZipFile(buf,'w') as z:
        z.writestr('[Content_Types].xml','<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"/>')
        z.writestr('xl/workbook.xml','<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>'+''.join(f'<sheet name="{escape(name)}" sheetId="{i}" r:id="rId{i}"/>' for i,name in enumerate(sheets,1))+'</sheets></workbook>')
        z.writestr('xl/_rels/workbook.xml.rels','<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'+''.join(f'<Relationship Id="rId{i}" Target="worksheets/sheet{i}.xml" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"/>' for i in range(1,len(sheets)+1))+'</Relationships>')
        for i,cells in enumerate(sheets.values(),1):
            rows={}
            for address,value in cells.items():
                number=int(''.join(c for c in address if c.isdigit()))
                cell=f'<c r="{address}" t="inlineStr"><is><t>{escape(value)}</t></is></c>' if isinstance(value,str) else f'<c r="{address}"><v>{value}</v></c>'
                rows.setdefault(number,[]).append(cell)
            xml='<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>'+''.join(f'<row r="{r}">'+''.join(c)+'</row>' for r,c in sorted(rows.items()))+'</sheetData></worksheet>'
            z.writestr(f'xl/worksheets/sheet{i}.xml',xml)
    return buf.getvalue()

def change_cell(data,sheet_name,address,value):
    ns={'s':'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
    output=io.BytesIO()
    with zipfile.ZipFile(io.BytesIO(data)) as source:
        wb=ET.fromstring(source.read('xl/workbook.xml'))
        sid=next(s.attrib['{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id'] for s in wb.find('s:sheets',ns) if s.attrib['name']==sheet_name)
        target=next(r.attrib['Target'] for r in ET.fromstring(source.read('xl/_rels/workbook.xml.rels')) if r.attrib['Id']==sid)
        target=target.lstrip('/') if target.startswith('/') else 'xl/'+target
        xml=ET.fromstring(source.read(target));cell=xml.find(f'.//s:c[@r="{address}"]',ns)
        for child in list(cell):cell.remove(child)
        cell.attrib.pop('t',None);ET.SubElement(cell,'{'+ns['s']+'}v').text=str(value)
        with zipfile.ZipFile(output,'w') as z:
            for name in source.namelist():z.writestr(name,ET.tostring(xml) if name==target else source.read(name))
    return output.getvalue()

def main():
    with tempfile.TemporaryDirectory(prefix='irei-board-test-') as folder:
        env=dict(os.environ,ASPNETCORE_ENVIRONMENT='Development',Evidence__Enabled='true',Evidence__Prototype='true',Evidence__AllowLocalHttp='true',Evidence__RequireAdminMfa='false',Evidence__EncryptionKey=base64.b64encode(os.urandom(32)).decode(),Evidence__BootstrapEmail='admin@example.test',Evidence__BootstrapPassword=portal.PASSWORD,Evidence__StoragePath=str(Path(folder)/'portal'),Irei__DataRoot=str(Path(folder)/'data'),Irei__AdminKey='synthetic-board-key')
        log=open(Path(folder)/'server.log','w')
        server=subprocess.Popen([os.environ.get('DOTNET_EXE','dotnet'),str(ROOT/'src/IreiMvp.Web/bin/Release/net8.0/IreiMvp.Web.dll'),'--contentRoot',str(ROOT/'src/IreiMvp.Web'),'--urls',BASE.replace('127.0.0.1','localhost')],cwd=ROOT,env=env,stdout=log,stderr=log)
        try:
            admin=portal.Client()
            for _ in range(150):
                try:admin.session();break
                except OSError:
                    if server.poll() is not None:raise RuntimeError(Path(folder,'server.log').read_text())
                    time.sleep(.1)
            admin.call('/api/evidence/login',{'email':'admin@example.test','password':portal.PASSWORD,'totp':''});admin.session()
            anonymous=portal.Client()
            fixture=(ROOT/'src/IreiMvp.Web/Data/Examples/IREI_Stage1_V4.2_NGOB_Real_Data_Test.xlsx').read_bytes()
            upload(anonymous,[('mapped.xlsx',fixture)],status=403)
            no_csrf=portal.Client();no_csrf.jar=admin.jar;no_csrf.opener=admin.opener
            upload(no_csrf,[('mapped.xlsx',fixture)],status=403)
            check(True,'Anonymous and missing-CSRF uploads remain blocked')
            result=upload(admin,[('mapped.xlsx',fixture)]);rid=result['id'];p='/api/submissions/board/assessments/'+rid
            result=admin.call(p)
            check(abs(metric(result,'NOI')-4082375.58315832)<.01,'FY27 income after costs matches original source controls')
            check(metric(result,'HOMES_TOTAL')==715 and result['summary']['buildings']==16,'715 projected homes and 16 stable property records')
            check(result['summary']['operating']==12 and result['summary']['planned']==4,'Winding-down property remains in operating scope: 12 / 4')
            check(all(a['action'] for a in result['proposedTracker']),'Every proposed action has a source-derived title, including canonical exception field names')
            check(result['summary']['open']==16 and result['summary']['resolved']==2 and result['summary']['accepted']==2,'Exception statuses remain 16 open, 2 resolved, 2 accepted')
            check(abs(result['summary']['newPayments']-12024633.969398635)<.01,'New development annual source payments are shown without validating building coverage')
            check(result['summary']['belowOne']==2 and result['summary']['forwardPeriods']==9,'Two of nine forward periods fall below break-even at full precision')
            check(abs(metric(result,'SHORTFALL_EX_PLACEHOLDER',True)-3376096.971565852)<.01,'No-subsidy fallback sized to $3.376M annual shortfall')
            check(metric(result,'DSCR',True)<1 and round(metric(result,'DSCR',True),3)==.996,'Near-one coverage never rounds to a false 1.00x pass')
            check(result['summary']['cities']==['Vancouver'] and all(not p['fields'].get('Total_Units') for p in result['view']['properties']),'Missing historical units and cities are not copied from screenshots')
            check(any(c['area']=='Reserve opening continuity' and c['state']=='Review required' for c in result['checks']),'Reserve opening discontinuity exposed independently of cash flow')
            mid=next(m['id'] for m in result['view']['currentMetrics'] if m['code']=='NOI')
            provenance=admin.call(p+'/metrics/'+mid)
            check(provenance['facts'] and any('CASH FLOW' in f['sourceLocation'] for f in provenance['facts']),'Headline figures drill through to original source-cell evidence')
            bridge=admin.call(p+'/options/test',{'year':'2029-2030','case':'Without placeholder','kind':'Bridge','amount':3376096.971565852,'additionalDebtService':0,'subsidyConfirmed':False})
            check(bridge['residualCashShortfall']<.01 and abs(bridge['coverageAfter']-.754135895)<.000001,'A bridge funds cash without pretending to improve operating coverage')
            raw=synthetic_source();s=upload(admin,[('original.xlsx',raw)],year='2026-2027');original=admin.call('/api/submissions/board/assessments/'+s['id'])
            check(metric(original,'NOI')==600 and metric(original,'DEBT_SERVICE')==400,'Original cash-flow model maps named controls without a populated IREI template')
            check(metric(original,'CASH_AFTER_DEBT')==200 and original['summary']['buildings']==2,'Original mapping is source-driven, with no demonstration data injection')
            check(original['summary']['fullYear']=='2027-2028' and metric(original,'PLACEHOLDER_SUBSIDY',True)==300,'Original subsidy label feeds the no-placeholder sensitivity')
            check(any(c['state']=='Not testable' and 'payment' in c['area'] for c in original['checks']),'Signed building payments do not become validated positive coverage')
            changed=change_cell(raw,'Portfolio source','D33',850)
            altered=upload(admin,[('changed.xlsx',changed)],year='2026-2027');altered=admin.call('/api/submissions/board/assessments/'+altered['id'])
            check(metric(altered,'NOI')==400 and metric(altered,'CASH_AFTER_DEBT')==0,'Changing uploaded source revenue recalculates NOI, cash, ratios and findings')
            check(any(c['area']=='Revenue components' and c['period']=='2026-2027' and c['state']=='Review required' for c in altered['checks']),'Changing only a control exposes its disagreement with component inputs')
            check(metric(admin.call('/api/submissions/board/assessments/'+s['id']),'NOI')==600,'A new upload preserves the original assessment baseline')
            upload(admin,[('one.xlsx',fixture),('duplicate.xlsx',fixture)],status=400)
            check(True,'Duplicate source files cannot be double counted')
            tenant=portal.new_tenant(admin,'Board role boundary');client,_=portal.invite(admin,tenant,'board-client@example.test');analyst,_=portal.invite(admin,tenant,'board-analyst@example.test',role='IreiAnalyst')
            client.call(p,status=403);analyst.call(p,status=403)
            check(True,'Client and scoped analyst roles cannot read global board assessments')
            changed=admin.call(p+'/configuration',{'currentYear':'2029-2030','expectedVersion':1},method='PUT')
            check(changed['summary']['currentYear']=='2029-2030' and metric(changed,'DSCR')<1,'Year selection updates headline results across the shared seven-page record')
            recorded=admin.call(p+'/actions',{'text':'Obtain current lender statements','owner':'Synthetic CFO','dueDate':'2026-12-01','status':'Proposed','basis':'RENEWAL','expectedVersion':2})
            check(len(recorded['view']['registers']['48_ACTIONS'])==1 and not recorded['view']['registers'].get('49_DECISIONS'),'Actions persist without inventing board approvals')
            if os.environ.get('BOARD_RAW_WORKBOOK'):
                raw_path=Path(os.environ['BOARD_RAW_WORKBOOK']);both=upload(admin,[("mapped.xlsx",fixture),(raw_path.name,raw_path.read_bytes())]);both=admin.call('/api/submissions/board/assessments/'+both['id'])
                check(both['summary']['uploadedFiles']==2 and abs(metric(both,'NOI')-4082375.58315832)<.01,'Both supplied workbooks produce the selected canonical control once, without doubling totals')
                check(any(c['area']=='Source cell / mapped input' and c['state']=='Pass' for c in both['checks']),'Raw source cells reconcile against the mapped assessment')
                raw_result=upload(admin,[(raw_path.name,raw_path.read_bytes())],year='2026-2027');raw_result=admin.call('/api/submissions/board/assessments/'+raw_result['id'])
                check(abs(metric(raw_result,'NOI')-4082375.58315832)<.01 and metric(raw_result,'HOMES_TOTAL')==715,'Uploaded original V18 alone reproduces financial headline values and selected homes')
                check(abs(metric(raw_result,'PLACEHOLDER_SUBSIDY',True)-3318983)<.01,'Original V18 placeholder identification is label-driven and reproduces $3.319M subsidy')
                # Browser snapshots use exactly the supplied two-file upload.
                browser_id=both['view']['context']['id']
            else:browser_id=rid
            if os.environ.get('BOARD_BROWSER_TEST'):
                browser_env=dict(os.environ,BOARD_TEST_URL=BASE.replace('127.0.0.1','localhost'),BOARD_TEST_PASSWORD=portal.PASSWORD,BOARD_TEST_ASSESSMENT=browser_id)
                subprocess.run(['node','tests/browser_board.cjs'],cwd=ROOT,env=browser_env,check=True)
            print(f'BOARD: {passed} integration checks passed.',flush=True)
        finally:
            server.terminate()
            try:server.wait(timeout=5)
            except subprocess.TimeoutExpired:server.kill()
            log.close()

if __name__=='__main__':main()
