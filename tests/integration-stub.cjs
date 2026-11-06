// Test-only loopback receiver. No external traffic or credentials are required.
const http=require('node:http');const fs=require('node:fs');const path=require('node:path');
const dir=path.resolve(process.argv[2]);if(!fs.statSync(dir).isDirectory())throw Error('Test directory missing');
const server=http.createServer((req,res)=>{let body='';req.on('data',chunk=>{body+=chunk;if(body.length>2_000_000)req.destroy();});req.on('end',()=>{
  if(req.url==='/employees.csv'){res.writeHead(200,{'Content-Type':'text/csv;charset=utf-8'});res.end(fs.readFileSync(path.join(dir,'employees.csv')));return;}
  if(req.url==='/webhook'){fs.appendFileSync(path.join(dir,'requests.jsonl'),JSON.stringify({headers:req.headers,body})+'\n');let config=JSON.parse(fs.readFileSync(path.join(dir,'response.json'),'utf8'));if(config.failures>0){config.failures--;fs.writeFileSync(path.join(dir,'response.json'),JSON.stringify(config));res.writeHead(503);res.end('retry');}else{res.writeHead(204);res.end();}return;}
  res.writeHead(200);res.end('ready');
});});server.listen(0,'127.0.0.1',()=>fs.writeFileSync(path.join(dir,'ready.json'),JSON.stringify({port:server.address().port})));
