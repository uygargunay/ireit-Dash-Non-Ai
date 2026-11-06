"""Production-mode gates with synthetic files, a temporary TLS certificate and a local scanner stub.
This validates protocol behavior, not hosting approval or actual malware-detection effectiveness.
"""
import base64
import os
from pathlib import Path
import socket
import socketserver
import struct
import subprocess
import tempfile
import threading
import time
import smoke_evidence as t


class Scanner(socketserver.StreamRequestHandler):
    calls = 0

    def handle(self):
        if self.rfile.read(10) != b'zINSTREAM\0':
            return
        while True:
            length = struct.unpack('>I', self.rfile.read(4))[0]
            if length == 0:
                break
            remaining = length
            while remaining:
                data = self.rfile.read(remaining)
                if not data:
                    return
                remaining -= len(data)
        Scanner.calls += 1
        self.wfile.write(b'stream: OK\0')


with tempfile.TemporaryDirectory(prefix='irei-evidence-prod-test-') as folder:
    cert, key, pfx = [str(Path(folder, name)) for name in ['certificate.pem', 'private.pem', 'certificate.pfx']]
    subprocess.run(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-keyout', key, '-out', cert, '-days', '1', '-subj', '/CN=localhost', '-addext', 'subjectAltName=DNS:localhost,IP:127.0.0.1'], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    subprocess.run(['openssl', 'pkcs12', '-export', '-out', pfx, '-inkey', key, '-in', cert, '-passout', 'pass:synthetic-cert-only'], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    with socket.socket() as probe:
        probe.bind(('127.0.0.1', 0))
        scanner_port = probe.getsockname()[1]
    command = __import__('json').loads(os.environ.get('EVIDENCE_TEST_SERVER', __import__('json').dumps([t.DOTNET, str(t.ROOT / f"src/IreiMvp.Web/bin/{os.environ.get('EVIDENCE_CONFIGURATION','Debug')}/net8.0/IreiMvp.Web.dll")])))
    command += ['--contentRoot', str(t.ROOT / 'src/IreiMvp.Web'), '--urls', 'http://127.0.0.1:5095;https://127.0.0.1:5096']
    env = dict(os.environ, ASPNETCORE_ENVIRONMENT='Production', Evidence__Enabled='true', Evidence__RequireAdminMfa='false', Evidence__Prototype='false', Evidence__AllowLocalHttp='false', Evidence__InfrastructureSpendBlocked='true', Evidence__StoragePath=str(Path(folder,'data')),
        Evidence__EncryptionKey=base64.b64encode(os.urandom(32)).decode(), Evidence__BootstrapEmail='admin@example.test', Evidence__BootstrapPassword=t.PASSWORD, Evidence__BootstrapTotpSecret='',
        Evidence__ScannerPort=str(scanner_port), ASPNETCORE_Kestrel__Certificates__Default__Path=pfx, ASPNETCORE_Kestrel__Certificates__Default__Password='synthetic-cert-only')
    log = open(Path(folder,'server.log'),'w')
    server = subprocess.Popen(command,cwd=t.ROOT,env=env,stdout=log,stderr=log)
    scanner = None
    try:
        t.BASE='https://127.0.0.1:5096'
        client=t.Client()
        for _ in range(100):
            try:
                client.session()
                break
            except OSError:
                if server.poll() is not None:
                    raise RuntimeError(Path(folder,'server.log').read_text())
                time.sleep(.1)
        else:
            raise RuntimeError('Production test host did not start')
        t.BASE='http://127.0.0.1:5095'
        client.call('/api/evidence/session',status=400)
        t.check(True,'Production portal rejects plaintext HTTP')
        t.BASE='https://127.0.0.1:5096'
        client.session()
        client.call('/api/evidence/login',{'email':'admin@example.test','password':t.PASSWORD,'totp':''})
        t.check(not client.session()['requireAdminMfa'],'Fresh production bootstrap signs in without an authenticator secret')
        t.check(all(cookie.secure for cookie in client.jar),'Production session and confirmation cookies require TLS')
        tenant=t.new_tenant(client,'Synthetic production-gate test')
        payload={'tenantId':tenant['id'],'scopeId':'organization','categoryIds':['02'],'coverage':'Partial','confirmedNoPersonalData':True,'files':[{'name':'synthetic.csv','size':4}]}
        client.call('/api/evidence/uploads/prepare',payload,status=400)
        t.check(True,'Live-mode intake is blocked without approved authority, hosting, security, backup and retention terms')
        t.policy(client,tenant)
        failed=t.upload(client,tenant,'synthetic.csv',b'a,b\n')
        t.check(failed['state']=='Quarantined','Unavailable scanner quarantines source instead of accepting it')
        client.call('/api/evidence/downloads',{'tenantId':tenant['id'],'versionIds':[failed['versionId']],'categoryId':'','propertyId':'','reportingPeriod':'','archive':False},status=400)
        t.check(True,'Unscanned source cannot be downloaded by an administrator')
        socketserver.TCPServer.allow_reuse_address=True
        scanner=socketserver.TCPServer(('127.0.0.1',scanner_port),Scanner)
        threading.Thread(target=scanner.serve_forever,daemon=True).start()
        scanned=client.call(f"/api/evidence/versions/{failed['versionId']}/rescan",{})
        t.check(scanned['processing']=='Accepted' and Scanner.calls==1,'Local scanner INSTREAM success clears screening without writing plaintext temporary files')
        t.check(next(v for v in t.workspace(client,tenant)['versions'] if v['id']==failed['versionId'])['validation']=='Unreviewed','Scanner clearance does not verify factual content')
        print(f'PRODUCTION GATES: {t.passed} synthetic checks passed.',flush=True)
    finally:
        if scanner:
            scanner.shutdown()
            scanner.server_close()
        server.terminate()
        try:
            server.wait(timeout=5)
        except subprocess.TimeoutExpired:
            server.kill()
        log.close()
