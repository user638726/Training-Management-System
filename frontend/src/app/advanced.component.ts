import {Component,DestroyRef,computed,inject,signal} from '@angular/core';
import {CommonModule} from '@angular/common';
import {FormsModule} from '@angular/forms';
import {ActivatedRoute,RouterLink} from '@angular/router';
import {HttpClient,HttpErrorResponse} from '@angular/common/http';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {Observable,finalize,map,of} from 'rxjs';
import {AuthService} from './auth.service';
import {ADVANCED_TABS,ADVANCED_TITLES,AdvancedSection,Field,TabDefinition} from './advanced.config';
type Row=Record<string,unknown>;type Value=string|number|boolean|null;interface Choice{value:number;label:string}interface Lookup{total:number;items:Choice[]}
interface Stage{name:string;kind:string;userId:number|null;dueHours:number}
@Component({selector:'app-advanced',standalone:true,imports:[CommonModule,FormsModule,RouterLink],templateUrl:'./advanced.component.html',styleUrl:'./advanced.component.css'})
export class AdvancedComponent{
  auth=inject(AuthService);private http=inject(HttpClient);private destroy=inject(DestroyRef);
  section=inject(ActivatedRoute).snapshot.data['advancedSection'] as AdvancedSection;
  title=ADVANCED_TITLES[this.section];tabs=ADVANCED_TABS[this.section];tab=signal<TabDefinition>(this.tabs[0]);
  private generation=0;private lookupGeneration:Record<string,number>={};
  loading=signal(false);busy=signal(false);error=signal('');message=signal('');rows=signal<Row[]>([]);total=signal(0);page=signal(1);search=signal('');
  form:Record<string,Value>={};files:Record<string,File>={};editing:Row|null=null;
  lookups=signal<Record<string,Lookup>>({});lookupSearch:Record<string,string>={};lookupPage:Record<string,number>={};private lookupTimers:Record<string,ReturnType<typeof setTimeout>>={};
  stages:Stage[]=[{name:'部門主管',kind:'Manager',userId:null,dueHours:48},{name:'人資確認',kind:'HR',userId:null,dueHours:48}];
  selectedGoalCourses:number[]=[];
  selectedScopes:string[]=['training.read'];scopeOptions=['scim.read','scim.write','learning.read','learning.write','training.read'];
  selectedEvents:string[]=['enrollment.completed'];eventOptions=['course.published','course.updated','enrollment.registered','enrollment.approved','enrollment.cancelled','enrollment.completed'];
  secret=signal('');secretLabel=signal('');comments:Record<string,string>={};groupDepartment:Record<string,number>={};video=signal<Row|null>(null);details=signal<Row[]|null>(null);
  statusLabels:Record<string,string>={Pending:'待處理',Waiting:'等待中',Approved:'已核准',Rejected:'已退回',Withdrawn:'已撤回',Expired:'已到期',Offered:'保留名額中',Accepted:'已接受',Cancelled:'已取消',Active:'進行中',Completed:'已完成',Submitted:'待審核',Paid:'已付款',Reserved:'預留',Consumed:'認列',Released:'已釋放',Settled:'已核銷',Missing:'缺少證照',Expiring:'即將到期',Valid:'符合',Revoked:'已撤銷',Success:'成功',Failed:'失敗',Running:'執行中',Never:'尚未執行',Delivered:'已寄送',Retry:'等待重試',Dead:'待人工處理',Disabled:'已停用',SelfReported:'本人自填',External:'外部教材',Video:'影音'};
  admin=computed(()=>this.auth.user()?.role==='Admin');reviewer=computed(()=>['Admin','HR','Manager'].includes(this.auth.user()?.role||''));
  allowed=computed(()=>!['provisioning','api-integrations'].includes(this.section)||this.admin());
  canCreate=computed(()=>!!this.tab().create&&(!this.tab().manageOnly||this.auth.canManage())&&(!this.tab().adminOnly||this.admin()));
  visibleRows=computed(()=>{const q=this.search().trim().toLowerCase();const rows=q?this.rows().filter(r=>Object.values(r).some(v=>typeof v==='string'&&v.toLowerCase().includes(q))):this.rows();return ['matrix','offerings'].includes(this.tab().id)?rows:rows.slice((this.page()-1)*20,this.page()*20);});
  constructor(){if(this.allowed())this.reset();this.destroy.onDestroy(()=>Object.values(this.lookupTimers).forEach(clearTimeout));}
  hasRequiredFiles(){return (this.tab().fields||[]).filter(f=>f.type==='file'&&f.required).every(f=>!!this.files[f.key]);}
  arrayLength(r:Row,key:string){return Array.isArray(r[key])?(r[key] as unknown[]).length:0;}
  courseNames(r:Row){return Array.isArray(r['recommendedCourseIds'])?(r['recommendedCourseIds'] as number[]).map(id=>this.label('courses',id)).join('、'):'';}
  s(r:Row,key:string){const value=r[key];return value==null?'':String(value);}
  n(r:Row,key:string){return Number(r[key]||0);}
  bool(r:Row,key:string){return r[key]===true;}
  nested(r:Row,key:string){return Array.isArray(r[key])?r[key] as Row[]:[];}
  display(r:Row,key:string,date=false){const value=r[key];if(value==null||value==='')return '—';if(date){const d=new Date(String(value));return Number.isNaN(d.getTime())?String(value):new Intl.DateTimeFormat('zh-TW',{dateStyle:'short',timeStyle:'short'}).format(d);}if(typeof value==='boolean')return value?'啟用':'停用';return this.statusLabels[String(value)]||String(value);}
  label(kind:string,id:unknown){return this.lookups()[kind]?.items.find(x=>x.value===Number(id))?.label||String(id??'—');}
  isOwn(r:Row){return this.n(r,'employeeId')===this.auth.user()?.employeeId;}
  switchTab(t:TabDefinition){this.tab.set(t);this.reset();}
  reset(){this.loading.set(false);this.editing=null;this.form={};this.files={};this.error.set('');this.secret.set('');this.video.set(null);this.details.set(null);this.page.set(1);this.search.set('');
    for(const field of this.tab().fields||[])this.form[field.key]=field.initial??(field.type==='checkbox'?false:null);
    if(this.tab().id==='delegations')this.form['principalUserId']=Number(this.auth.user()?.id||0);
    if(this.tab().id==='goals'){this.form['employeeId']=this.auth.user()?.employeeId??null;this.selectedGoalCourses=[];this.lookup('courses');}
    if(this.tab().id==='policies')this.stages=[{name:'部門主管',kind:'Manager',userId:null,dueHours:48},{name:'人資確認',kind:'HR',userId:null,dueHours:48}];
    for(const kind of new Set((this.canCreate()||this.tab().id==='ledger'?this.tab().fields||[]:[]).map(f=>f.lookup).filter((v):v is string=>!!v)))this.lookup(kind);
    if(this.tab().id==='delegations'||this.tab().id==='policies'&&this.auth.canManage())this.lookup('users');if(this.tab().id==='groups')this.lookup('departments');if(this.tab().id==='matrix')this.lookup('courses');this.load();
  }
  lookup(kind:string,page=1){this.lookupPage[kind]=page;const sequence=(this.lookupGeneration[kind]||0)+1;this.lookupGeneration[kind]=sequence;this.http.get<Lookup>('/api/advanced/lookups',{params:{kind,page,search:this.lookupSearch[kind]||''}}).pipe(takeUntilDestroyed(this.destroy)).subscribe({next:result=>{if(sequence!==this.lookupGeneration[kind])return;this.lookups.update(all=>({...all,[kind]:{...result,items:page===1?result.items:[...(all[kind]?.items||[]),...result.items]}}));},error:()=>{}});}
  searchLookup(kind:string,text:string){this.lookupSearch[kind]=text;clearTimeout(this.lookupTimers[kind]);this.lookupTimers[kind]=setTimeout(()=>this.lookup(kind),250);}
  moreLookup(kind:string){this.lookup(kind,(this.lookupPage[kind]||1)+1);}
  load(){
    this.error.set('');const sequence=++this.generation;const t=this.tab();let request:Observable<{rows:Row[];total:number}>;
    if(['settings','meetings','ledger'].includes(t.id)&&!this.form[t.id==='ledger'?'budgetId':'courseId']){this.rows.set([]);this.total.set(0);return;}
    if(t.endpoint==='course-settings'){this.http.get<Row>('/api/courses/'+this.form['courseId']).pipe(takeUntilDestroyed(this.destroy)).subscribe({next:c=>{if(sequence!==this.generation)return;this.form={...this.form,seriesId:c['seriesId'] as Value,policyId:c['approvalPolicyId'] as Value,budgetId:c['budgetId'] as Value,checkScheduleConflicts:c['checkScheduleConflicts'] as Value,rowVersion:c['rowVersion'] as Value};},error:e=>this.fail(e)});return;}
    if(t.endpoint==='course-offerings')request=this.http.get<Row[]>('/api/courses',{params:{page:this.page(),pageSize:20},observe:'response'}).pipe(map(r=>({rows:r.body||[],total:Number(r.headers.get('X-Total-Count')||0)})));
    else{let path=t.endpoint;if(t.id==='meetings')path=`courses/${this.form['courseId']}/meetings`;if(t.id==='ledger')path=`budgets/${this.form['budgetId']}/ledger`;
      if(t.id==='matrix')request=this.http.get<{items:Row[];total:number}>('/api/advanced/'+path,{params:{page:this.page(),search:this.search()}}).pipe(map(v=>({rows:v.items,total:v.total})));
      else request=this.http.get<Row[]>('/api/advanced/'+path).pipe(map(rows=>({rows,total:rows.length})));}
    this.loading.set(true);request.pipe(takeUntilDestroyed(this.destroy),finalize(()=>{if(sequence===this.generation)this.loading.set(false);})).subscribe({next:v=>{if(sequence!==this.generation)return;this.rows.set(v.rows);this.total.set(v.total);},error:e=>this.fail(e)});
  }
  fieldChanged(field:Field){if(field.key==='courseId'&&['settings','meetings'].includes(this.tab().id)||field.key==='budgetId'&&this.tab().id==='ledger')this.load();}
  changePage(delta:number){this.page.update(p=>p+delta);if(['matrix','offerings'].includes(this.tab().id))this.load();}
  searchRows(){this.page.set(1);if(this.tab().id==='matrix')this.load();}
  selectFile(field:Field,event:Event){const input=event.target as HTMLInputElement;const file=input.files?.[0];if(!file)return;if(file.size>10_000_000){this.error.set('附件不可超過 10 MB。');return;}this.files[field.key]=file;}
  toggle(values:string[],value:string,checked:boolean){const index=values.indexOf(value);if(checked&&index<0)values.push(value);if(!checked&&index>=0)values.splice(index,1);}
  addStage(){if(this.stages.length<10)this.stages.push({name:'新增關卡',kind:'User',userId:null,dueHours:48});}
  private body(){const body:Record<string,unknown>={};for(const field of this.tab().fields||[]){let value=this.form[field.key];if(field.type==='file')continue;if(value===''||value==null){if(field.lookup||field.type==='number'||field.type==='date'||field.type==='datetime-local')body[field.key]=null;continue;}if(field.type==='date'||field.type==='datetime-local'){const d=new Date(String(value)+(field.type==='date'?'T00:00:00':''));if(field.key==='expiresAt'&&field.type==='date'){d.setDate(d.getDate()+1);d.setMilliseconds(d.getMilliseconds()-1);}body[field.key]=d.toISOString();}else body[field.key]=value;}
    if(this.tab().id==='policies')body['stages']=this.stages.map(s=>({...s}));if(this.tab().id==='clients')body['scopes']=this.selectedScopes;if(this.tab().id==='webhooks')body['events']=this.selectedEvents;if(this.tab().id==='goals')body['courseIds']=this.selectedGoalCourses;
    if(this.editing)body['rowVersion']=this.editing['rowVersion'];return body;
  }
  submit(){
    if(this.busy())return;let body:Record<string,unknown>;try{body=this.body();}catch{this.error.set('請確認日期與表單內容。');return;}
    let path=this.tab().create;if(!path)return;let method='POST';
    if(path==='offering')path=`series/${this.form['seriesId']}/offerings`;
    if(path==='meeting')path=`courses/${this.form['courseId']}/meetings`;
    if(path==='course-settings'){path=`courses/${this.form['courseId']}/advanced`;body['rowVersion']=this.form['rowVersion'];method='PUT';}
    if(path==='activity-progress')path=`activities/${this.form['activityId']}/progress`;
    if(this.editing&&['budgets','policies','goals'].includes(path)){path+='/'+this.editing['id'];method='PUT';}
    let payload:unknown=body;if(Object.values(this.files).length){const form=new FormData();for(const [key,value]of Object.entries(body))if(value!=null)form.append(key,String(value));for(const [key,file]of Object.entries(this.files))form.append(key,file);payload=form;}
    this.action(this.http.request<Row>(method,'/api/advanced/'+path,{body:payload}),'資料已儲存。',result=>{if(result['token']||result['secret']){this.secret.set(String(result['token']||result['secret']));this.secretLabel.set(result['token']?'服務金鑰（僅顯示一次）':'Webhook 簽章秘密（僅顯示一次）');}this.editing=null;this.files={};this.load();});
  }
  edit(row:Row){this.editing=row;for(const field of this.tab().fields||[]){const value=row[field.key];this.form[field.key]=field.type==='date'?String(value||'').slice(0,10):field.type==='datetime-local'&&value?this.localDate(String(value)):value as Value??field.initial??null;}if(this.tab().id==='goals')this.selectedGoalCourses=JSON.parse(this.s(row,'courseIdsJson')||'[]') as number[];if(this.tab().id==='policies'){try{const raw=JSON.parse(this.s(row,'stagesJson')) as Row[];this.stages=raw.map(v=>({name:String(v['Name']??v['name']??''),kind:String(v['Kind']??v['kind']??'Manager'),userId:(v['UserId']??v['userId']??null) as number|null,dueHours:Number(v['DueHours']??v['dueHours']??48)}));}catch{this.stages=[];}}}
  toggleGoalCourse(id:number,checked:boolean){this.selectedGoalCourses=checked?[...this.selectedGoalCourses,id]:this.selectedGoalCourses.filter(x=>x!==id);}
  private localDate(value:string){const d=new Date(value);const pad=(n:number)=>String(n).padStart(2,'0');return `${d.getFullYear()}-${pad(d.getMonth()+1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;}
  action<T>(request:Observable<T>,success:string,callback?:(value:T)=>void){if(this.busy())return;this.busy.set(true);this.error.set('');this.message.set('');request.pipe(takeUntilDestroyed(this.destroy),finalize(()=>this.busy.set(false))).subscribe({next:v=>{this.message.set(success);if(callback)callback(v);else this.load();},error:e=>this.fail(e)});}
  post(path:string,body:unknown={}){return this.http.post<Row>('/api/advanced/'+path,body);}
  review(row:Row,approve:boolean,resource:string){const id=this.s(row,'id');this.action(this.post(`${resource}/${id}/decision`,{approve,comment:this.comments[id]||'',rowVersion:row['rowVersion']}),approve?'審核已核准；多階流程將依序往下一關。':'申請已退回。');}
  command(path:string,message:string){this.action(this.post(path),message);}
  paid(row:Row){this.action(this.post(`expenses/${row['id']}/paid`,{rowVersion:row['rowVersion']}),'已記錄付款。');}
  revokeCredential(row:Row){const comment=this.comments[this.s(row,'id')]||'';if(!comment.trim()){this.error.set('撤銷證照須填寫原因。');return;}this.action(this.post(`credentials/${row['id']}/revoke`,{comment,rowVersion:row['rowVersion']}),'證照已撤銷，歷史紀錄保留。');}
  rotate(row:Row){this.action(this.post(`clients/${row['id']}/rotate`,{rowVersion:row['rowVersion']}),'舊金鑰已失效。',v=>{this.secret.set(String(v['token']));this.secretLabel.set('新服務金鑰（僅顯示一次）');this.load();});}
  bindGroup(row:Row){const id=this.s(row,'id');this.action(this.post(`provisioning-groups/${id}/department`,{departmentId:this.groupDepartment[id]}),'部門對應與成員已更新。');}
  attempts(row:Row){this.action(this.http.get<Row[]>(`/api/advanced/webhook-deliveries/${row['id']}/attempts`),'已載入寄送紀錄。',v=>this.details.set(v));}
  launch(row:Row){this.video.set(row);}
  download(path:string,name:string){this.action(this.http.get('/api/advanced/'+path,{responseType:'blob'}),'檔案已下載。',blob=>{const url=URL.createObjectURL(blob);const a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);});}
  private fail(e:HttpErrorResponse){if(e.error instanceof Blob){void e.error.text().then(text=>{try{this.error.set(JSON.parse(text).title||'下載失敗。');}catch{this.error.set('下載失敗。');}});return;}this.error.set(e.error?.errors?Object.values(e.error.errors).flat().join(' '):e.error?.title||e.error?.detail||'無法完成操作，請重新整理後重試。');}
}
