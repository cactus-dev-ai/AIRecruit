(() => {
  const $ = id => document.getElementById(id);
  let token = sessionStorage.getItem('interviewToken') || '';
  let room, trtc, micStream, recorder, questionIndex = 0, recording = false, answerArmed = false;
  let readingQuestion = false, questionReadToken = 0, startedAt = 0, timerHandle;

  async function api(url, options = {}) {
    options.headers = {...(options.headers || {})};
    if (token) options.headers['X-Interview-Token'] = token;
    const response = await fetch(url, options);
    if (!response.ok) {
      let body = {}; try { body = await response.json(); } catch {}
      throw new Error(body.message || `请求失败（${response.status}）`);
    }
    return response.status === 204 ? null : response.json();
  }
  function busy(button, state, text) { button.disabled = state; if (text) button.textContent = text; }
  function toast(message) { $('toast').textContent = message; $('toast').hidden = false; clearTimeout(toast.t); toast.t = setTimeout(() => $('toast').hidden = true, 5000); }
  function show(card) { ['verify-card','setup-card','question-card','report-card'].forEach(id => $(id).hidden = id !== card); }
  function speakQuestion(text, token) {
    const synth = window.speechSynthesis;
    const Utterance = window.SpeechSynthesisUtterance;
    if (!synth || !Utterance) return Promise.resolve();
    synth.cancel();
    return new Promise(resolve => {
      const utterance = new Utterance(text);
      let completed = false;
      const finish = () => { if (completed) return; completed = true; clearTimeout(fallback); resolve(); };
      utterance.lang = 'zh-CN'; utterance.rate = .95;
      utterance.onend = finish; utterance.onerror = finish;
      const fallback = setTimeout(finish, Math.max(15000, text.length * 650));
      if (token !== questionReadToken) return finish();
      synth.speak(utterance);
    });
  }

  $('verify-button').addEventListener('click', async () => {
    const button = $('verify-button'); busy(button, true, '正在验证…');
    try {
      const result = await api('/api/public/interview/verify', {method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify({phone:$('phone').value, code:$('code').value})});
      token = result.accessToken; sessionStorage.setItem('interviewToken', token);
      $('candidate').textContent = result.candidateName; $('job').textContent = result.jobName; show('setup-card');
      const resultSupport = window.TRTC ? await TRTC.isSupported() : false;
      const supported = typeof resultSupport === 'boolean' ? resultSupport : !!resultSupport?.result;
      $('support').textContent = supported ? '浏览器支持 TRTC Web 音频通话' : '当前浏览器不支持 TRTC，请使用最新版微信或 Chrome。';
      $('support').classList.toggle('ok', supported);
    } catch (error) { toast(error.message); }
    finally { busy(button, false, '验证并继续'); }
  });

  $('start-button').addEventListener('click', async () => {
    const button = $('start-button');
    if (!$('consent').checked) return toast('请先阅读并同意录音与 AI 处理说明。');
    busy(button, true, '正在连接音频房间…');
    try {
      micStream = await navigator.mediaDevices.getUserMedia({audio:{channelCount:1, echoCancellation:true, noiseSuppression:true, autoGainControl:true}, video:false});
      const device = {microphone:true, userAgent:navigator.userAgent, sampleRate:micStream.getAudioTracks()[0]?.getSettings?.().sampleRate || null};
      room = await api('/api/public/interview/start', {method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify({consentGranted:true,clientInfo:navigator.userAgent,deviceCheckJson:JSON.stringify(device)})});
      trtc = TRTC.create();
      await trtc.enterRoom({sdkAppId:room.sdkAppId,userId:room.userId,userSig:room.userSig,strRoomId:room.roomId,scene:'rtc'});
      await trtc.startLocalAudio();
      await api('/api/public/interview/recording/start', {method:'POST'});
      questionIndex = Math.max(0, room.questions.findIndex(q => !q.completed));
      show('question-card'); renderQuestion();
    } catch (error) { toast(`启动失败：${error.message}`); try { await trtc?.exitRoom(); } catch {} }
    finally { busy(button, false, '检测麦克风并开始'); }
  });

  async function renderQuestion() {
    const q = room.questions[questionIndex];
    const token = ++questionReadToken;
    answerArmed = false; readingQuestion = true;
    $('progress').textContent = `第 ${questionIndex + 1} / ${room.questions.length} 题`;
    $('question-text').textContent = q.questionText; $('timer').textContent = '00:00';
    $('answer-state').textContent = `正在朗读题目，可提前点击“开始回答”；读题结束后将立即录音。本题最多 ${q.maxAnswerSeconds} 秒。`;
    $('transcript').hidden = true; $('next-button').hidden = true; $('record-button').hidden = false; $('record-button').textContent = '开始回答';
    await speakQuestion(q.questionText, token);
    if (token !== questionReadToken) return;
    readingQuestion = false;
    if (answerArmed) await startAnswer();
    else $('answer-state').textContent = `题目朗读完毕，请立即开始回答。本题最多 ${q.maxAnswerSeconds} 秒。`;
  }

  $('record-button').addEventListener('click', async () => {
    if (recording) return stopAnswer();
    if (readingQuestion) {
      answerArmed = true;
      $('record-button').textContent = '已准备，等待读题结束';
      $('answer-state').textContent = '已进入作答准备；题目朗读结束后将自动开始录音和计时。';
      return;
    }
    await startAnswer();
  });

  async function startAnswer() {
    if (recording) return;
    try {
      answerArmed = false; window.speechSynthesis?.cancel(); recorder = new WavRecorder(micStream); await recorder.start(); recording = true; startedAt = Date.now();
      $('record-button').textContent = '结束回答'; $('answer-state').textContent = '正在录音，请清晰作答…'; $('wave').classList.add('active');
      timerHandle = setInterval(() => { const seconds = Math.floor((Date.now()-startedAt)/1000); $('timer').textContent = `${String(Math.floor(seconds/60)).padStart(2,'0')}:${String(seconds%60).padStart(2,'0')}`; if(seconds >= room.questions[questionIndex].maxAnswerSeconds) stopAnswer(); }, 250);
    } catch (error) { toast(`无法录音：${error.message}`); }
  }

  async function stopAnswer() {
    if (!recording) return; recording = false; clearInterval(timerHandle); $('wave').classList.remove('active');
    const button = $('record-button'); busy(button,true,'正在识别语音…');
    try {
      const {blob,durationMs} = await recorder.stop(); const form = new FormData(); form.append('audio',blob,'answer.wav'); form.append('durationMs',String(durationMs));
      const q = room.questions[questionIndex]; const result = await api(`/api/public/interview/turns/${q.turnId}/answer`,{method:'POST',body:form});
      $('answer-state').textContent='语音识别完成'; $('transcript').textContent=`识别结果：${result.transcript}`; $('transcript').hidden=false;
      $('record-button').hidden=true; $('next-button').hidden=false; $('next-button').textContent=questionIndex===room.questions.length-1?'提交面试':'下一题';
    } catch(error) { toast(error.message); $('answer-state').textContent='识别失败，可以重新回答。'; }
    finally { busy(button,false,'重新回答'); }
  }

  $('next-button').addEventListener('click', async () => {
    if(questionIndex < room.questions.length-1){ questionIndex++; renderQuestion(); return; }
    const button=$('next-button');busy(button,true,'正在生成报告…');
    try{
      const report=await api('/api/public/interview/submit',{method:'POST'});
      try{await trtc?.stopLocalAudio();await trtc?.exitRoom();}catch{}
      micStream?.getTracks().forEach(t=>t.stop());sessionStorage.removeItem('interviewToken');
      $('report-summary').textContent=report.summary;$('report-status').textContent=report.status;$('recommendation').textContent=report.recommendation;$('concerns').textContent=report.concerns;show('report-card');
    }catch(error){toast(error.message);busy(button,false,'提交面试');}
  });

  class WavRecorder {
    constructor(stream){this.stream=stream;this.chunks=[];}
    async start(){
      this.context=new (window.AudioContext||window.webkitAudioContext)();this.source=this.context.createMediaStreamSource(this.stream);
      this.processor=this.context.createScriptProcessor(4096,1,1);this.sampleRate=this.context.sampleRate;this.started=performance.now();
      this.processor.onaudioprocess=e=>this.chunks.push(new Float32Array(e.inputBuffer.getChannelData(0)));
      this.source.connect(this.processor);this.processor.connect(this.context.destination);
    }
    async stop(){
      const durationMs=Math.max(300,Math.round(performance.now()-this.started));this.processor.disconnect();this.source.disconnect();await this.context.close();
      const total=this.chunks.reduce((n,x)=>n+x.length,0),merged=new Float32Array(total);let offset=0;for(const x of this.chunks){merged.set(x,offset);offset+=x.length;}
      const pcm=downsample(merged,this.sampleRate,16000),buffer=new ArrayBuffer(44+pcm.length*2),view=new DataView(buffer);
      write(view,0,'RIFF');view.setUint32(4,36+pcm.length*2,true);write(view,8,'WAVE');write(view,12,'fmt ');view.setUint32(16,16,true);view.setUint16(20,1,true);view.setUint16(22,1,true);view.setUint32(24,16000,true);view.setUint32(28,32000,true);view.setUint16(32,2,true);view.setUint16(34,16,true);write(view,36,'data');view.setUint32(40,pcm.length*2,true);
      for(let i=0;i<pcm.length;i++){const s=Math.max(-1,Math.min(1,pcm[i]));view.setInt16(44+i*2,s<0?s*0x8000:s*0x7fff,true);}
      return{blob:new Blob([buffer],{type:'audio/wav'}),durationMs};
    }
  }
  function downsample(input,from,to){if(from===to)return input;const ratio=from/to,length=Math.round(input.length/ratio),output=new Float32Array(length);for(let i=0;i<length;i++){const start=Math.round(i*ratio),end=Math.min(input.length,Math.round((i+1)*ratio));let sum=0;for(let j=start;j<end;j++)sum+=input[j];output[i]=sum/Math.max(1,end-start);}return output;}
  function write(view,offset,text){for(let i=0;i<text.length;i++)view.setUint8(offset+i,text.charCodeAt(i));}
  window.addEventListener('beforeunload',()=>{questionReadToken++;window.speechSynthesis?.cancel();micStream?.getTracks().forEach(t=>t.stop());});
})();
