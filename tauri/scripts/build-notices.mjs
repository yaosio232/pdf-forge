import { readFile, readdir, mkdir, copyFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
const lock = JSON.parse(await readFile('package-lock.json','utf8'));
const output = 'public/licenses';
await mkdir(output, { recursive:true });
const packages=[];
for(const [location,entry] of Object.entries(lock.packages)) {
  if(!location.startsWith('node_modules/') || entry.dev) continue;
  let metadata;
  try {metadata=JSON.parse(await readFile(path.join(location,'package.json'),'utf8'));} catch {continue;}
  const target=path.join(output,'npm',`${metadata.name.replaceAll('/','_')}-${metadata.version}`);
  await mkdir(target,{recursive:true});
  const notices=[];
  for(const file of await readdir(location,{withFileTypes:true})) {
    if(file.isFile() && /^(licen[cs]e|copying|notice|unlicense)/i.test(file.name)) {
      await copyFile(path.join(location,file.name),path.join(target,file.name));
      notices.push(path.join(target,file.name).replaceAll('\\','/'));
    }
  }
  packages.push({name:metadata.name,version:metadata.version,license:metadata.license,files:notices});
}
await writeFile(path.join(output,'npm-manifest.json'),JSON.stringify(packages,null,2));
