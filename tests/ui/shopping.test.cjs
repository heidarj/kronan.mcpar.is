const {test, before, after} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {chromium} = require('playwright');
const html = fs.readFileSync(path.join(__dirname, '../../src/Kronan.mcpar.is/Ui/shopping.html'), 'utf8');
let browser;
before(async () => {
  browser = await chromium.launch({headless: true, ...(process.env.CHROMIUM_EXECUTABLE_PATH ? {executablePath:process.env.CHROMIUM_EXECUTABLE_PATH} : {})});
});
after(async () => { await browser?.close(); });
async function card({width=390, canWrite=true, theme='light', products}={}) {
  const page = await browser.newPage({viewport:{width,height:850}});
  const errors=[]; page.on('pageerror', error=>errors.push(error.message));
  await page.setContent('<style>body{margin:0}iframe{border:0;width:100%;height:800px}</style><iframe title="Shopping"></iframe>');
  await page.evaluate(({html,canWrite,theme,products}) => {
    const frame=document.querySelector('iframe');
    window.calls=[]; window.failure=null; window.prepared=0;
    window.note={name:'Household shopping',lines:[{token:'a0000000-0000-0000-0000-000000000001',text:'Butter',quantity:1},
      {token:'a0000000-0000-0000-0000-000000000002',text:'<img src=x onerror=alert(1)>',quantity:2}]};
    const initial={structuredContent:products ? {products,canWrite} : {note:window.note,canWrite}};
    window.addEventListener('message', event=>{
      if(event.source!==frame.contentWindow) return;
      const msg=event.data;
      const respond=result=>frame.contentWindow.postMessage({jsonrpc:'2.0',id:msg.id,result},'*');
      if(msg.method==='ui/initialize') {
        window.initialize=msg.params;
        respond({protocolVersion:'2026-01-26',hostContext:{theme},hostCapabilities:{}});
      }
      if(msg.method==='ui/notifications/initialized') frame.contentWindow.postMessage({jsonrpc:'2.0',method:'ui/notifications/tool-result',params:initial},'*');
      if(msg.method==='tools/call') {
        const call=msg.params; window.calls.push(call);
        if(call.name==='PrepareShoppingChange') {respond({structuredContent:{prepared:{operationId:'operation-'+(++window.prepared)}}});return;}
        if(call.name==='GetShoppingNote') {respond({structuredContent:{note:window.note}});return;}
        if(window.failure) {const error=window.failure;window.failure=null;respond({isError:true,structuredContent:{error}});return;}
        if(call.name==='AddShoppingItems') window.note.lines.push({token:'a0000000-0000-0000-0000-000000000003',text:call.arguments.items[0].text||'Selected product',quantity:1});
        if(call.name==='RemoveShoppingItem') window.note.lines=window.note.lines.filter(l=>l.token!==call.arguments.lineToken);
        respond({structuredContent:{note:window.note}});
      }
    });
    frame.srcdoc=html;
  },{html,canWrite,theme,products});
  const frame=page.frames()[1] || await new Promise(resolve=>page.once('frameattached',resolve));
  await frame.locator('#title').filter({hasText:products?'Selected products':'Household shopping'}).waitFor();
  await frame.locator('#refresh:not(:disabled)').waitFor();
  return {page,frame,errors};
}
test('mobile card escapes API text, uses the bridge, and adds one item', async()=>{
  const {page,frame,errors}=await card();
  try {
    assert.equal(await frame.locator('#content img').count(),0);
    assert.ok((await frame.locator('#content').innerText()).includes('<img src=x onerror=alert(1)>'));
    assert.equal(await frame.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth),true);
    assert.equal(await page.evaluate(()=>window.initialize.protocolVersion),'2026-01-26');
    assert.equal(await page.evaluate(()=>window.initialize.appInfo.version),'0.1.3');
    await frame.locator('#item').fill('Milk'); await frame.locator('#add-form button').click();
    await frame.locator('#status').filter({hasText:'Shopping note updated'}).waitFor();
    const calls=await page.evaluate(()=>window.calls);
    assert.deepEqual(calls.map(c=>c.name),['PrepareShoppingChange','AddShoppingItems']);
    assert.equal(calls[1].arguments.operationId,'operation-1');
    assert.equal(await frame.locator('li').count(),3);
    assert.deepEqual(errors,[]);
    if(process.env.SCREENSHOT_DIR) await page.screenshot({path:path.join(process.env.SCREENSHOT_DIR,'shopping-mobile.png')});
  } finally {await page.close();}
});
test('removal requires inline confirmation and cancellation makes no call',async()=>{
  const {page,frame}=await card({width:320});
  try {
    await frame.getByRole('button',{name:'Remove Butter',exact:true}).click();
    assert.equal((await page.evaluate(()=>window.calls)).length,0);
    await frame.getByRole('button',{name:'Cancel',exact:true}).click();
    assert.equal((await page.evaluate(()=>window.calls)).length,0);
    await frame.getByRole('button',{name:'Remove Butter',exact:true}).click();
    assert.equal(await frame.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth),true);
    await frame.getByRole('button',{name:'Confirm removal',exact:true}).click();
    await frame.locator('#status').filter({hasText:'Shopping note updated'}).waitFor();
    assert.equal(await frame.locator('li').count(),1);
  }finally {await page.close();}
});
test('unknown write blocks further changes until explicit refresh',async()=>{
  const {page,frame}=await card();
  try {
    await page.evaluate(()=>window.failure={code:'outcome_unknown',message:'Inspect the list.',outcomeUnknown:true});
    await frame.locator('#item').fill('Eggs'); await frame.locator('#add-form button').click();
    await frame.locator('#status').filter({hasText:'Inspect the list'}).waitFor();
    assert.equal(await frame.locator('#item').isDisabled(),true);
    assert.equal(await frame.locator('#refresh').isEnabled(),true);
    await frame.locator('#refresh').click();
    await frame.locator('#status').filter({hasText:'Shopping note refreshed'}).waitFor();
    assert.equal(await frame.locator('#item').isEnabled(),true);
    assert.equal(await page.evaluate(()=>window.prepared),1);
  }finally {await page.close();}
});
test('a known pre-dispatch failure retries the same operation ID',async()=>{
  const {page,frame}=await card();
  try {
    await page.evaluate(()=>window.failure={code:'upstream_cooldown',message:'Wait.',retryAfterSeconds:2});
    await frame.locator('#item').fill('Eggs'); await frame.locator('#add-form button').click();
    await frame.locator('#status').filter({hasText:'Try again in 2 seconds'}).waitFor();
    await frame.locator('#add-form button').click();
    await frame.locator('#status').filter({hasText:'Shopping note updated'}).waitFor();
    const calls=await page.evaluate(()=>window.calls);
    assert.equal(calls.filter(c=>c.name==='PrepareShoppingChange').length,1);
    assert.equal(calls[1].arguments.operationId,calls[2].arguments.operationId);
  }finally {await page.close();}
});
test('read-only card disables mutations',async()=>{
  const {page,frame}=await card({canWrite:false});
  try {assert.equal(await frame.locator('#add-form').isHidden(),true);assert.equal(await frame.locator('.quantity').first().isDisabled(),true);}
  finally {await page.close();}
});
test('dark product cards display a zero sale price and add a SKU',async()=>{
  const {page,frame,errors}=await card({width:680,theme:'dark',products:[{sku:'123',name:'Butter',price:499,onSale:true,discountedPrice:0,thumbnail:'javascript:alert(1)'}]});
  try {
    assert.equal(await frame.locator('html').getAttribute('data-theme'),'dark');
    assert.equal(await frame.locator('img').count(),0);
    assert.match(await frame.locator('.price').innerText(),/0/);
    if(process.env.SCREENSHOT_DIR) await page.screenshot({path:path.join(process.env.SCREENSHOT_DIR,'products-dark.png')});
    await frame.getByRole('button',{name:'Add to shopping note',exact:true}).click();
    await frame.locator('#status').filter({hasText:'Shopping note updated'}).waitFor();
    assert.equal((await page.evaluate(()=>window.calls))[1].arguments.items[0].sku,'123');
    assert.deepEqual(errors,[]);
  }finally {await page.close();}
});
