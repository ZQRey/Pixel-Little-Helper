/* Shared plain-text message syntax. No HTML input is evaluated. */
(() => {
  const marks = [['***',3],['**',1],['__',4],['~~',8],['*',2]];
  const alignment = /^\[align=(left|center|right)\]([\s\S]*)\[\/align\]$/;
  function inline(text, style=0, depth=0) {
    if (depth>=8) return [{text,style}];
    const result=[]; let plain='';
    const flush=()=>{if(plain){result.push({text:plain,style});plain='';}};
    for(let i=0;i<text.length;){
      if(text[i]==='\\' && i+1<text.length && '\\*`_~[]'.includes(text[i+1])){plain+=text[i+1];i+=2;continue;}
      if(text[i]==='`'){
        const end=text.indexOf('`',i+1);
        if(end>i+1){flush();result.push({text:text.slice(i+1,end),style:style|16});i=end+1;continue;}
      }
      let matched=false;
      for(const [mark,flag] of marks){
        if(!text.startsWith(mark,i))continue;
        let end=i+mark.length;
        do{end=text.indexOf(mark,end);if(end<0||text[end-1]!=='\\')break;end+=mark.length;}while(true);
        if(end<=i+mark.length)continue;
        flush();result.push(...inline(text.slice(i+mark.length,end),style|flag,depth+1));
        i=end+mark.length;matched=true;break;
      }
      if(!matched)plain+=text[i++];
    }
    flush();return result;
  }
  function parse(text){
    let fenced=false;const result=[];
    for(let line of text.replace(/\r\n/g,'\n').replace(/\r/g,'\n').split('\n')){
      const match=line.match(alignment);let align='left';
      if(match){align=match[1];line=match[2];}
      if(line==='```'){fenced=!fenced;continue;}
      result.push({fragments:fenced?[{text:line,style:16}]:inline(line),alignment:align,codeBlock:fenced});
    }
    return result;
  }
  function checked(text,start,length){
    if(text.length>4000)throw Error('Сообщение вместе с разметкой не должно превышать 4000 символов.');
    return{text,start,length};
  }
  function toggle(text,start,length,marker){
    if(marker==='```'){
      const from=start===0?0:text.lastIndexOf('\n',start-1)+1;
      let end=start+length;if(length>0&&text[end-1]==='\n')end--;
      let to=text.indexOf('\n',end);if(to<0)to=text.length;start=from;length=to-from;
    }
    const selected=text.slice(start,start+length),open=marker==='```'?'```\n':marker,close=marker==='```'?'\n```':marker;
    let from=start,count=length,replacement,caret;
    let beforeStars=0,afterStars=0;
    for(let i=start-1;i>=0&&text[i]==='*';i--)beforeStars++;
    for(let i=start+length;i<text.length&&text[i]==='*';i++)afterStars++;
    const italicSelected=marker!=='*'||!selected.startsWith('**')||selected.startsWith('***');
    const italicSurround=marker!=='*'||beforeStars%2===1&&afterStars%2===1;
    if(italicSelected&&length>=open.length+close.length&&selected.startsWith(open)&&selected.endsWith(close)){
      replacement=selected.slice(open.length,-close.length);caret=start;
    }else if(italicSurround&&start>=open.length&&text.slice(start-open.length,start)===open&&text.slice(start+length,start+length+close.length)===close){
      from-=open.length;count+=open.length+close.length;replacement=selected;caret=from;
    }else{replacement=open+selected+close;caret=start+open.length;}
    return checked(text.slice(0,from)+replacement+text.slice(from+count),caret,length===0?0:replacement.startsWith(open)?selected.length:replacement.length);
  }
  function align(text,start,length,value,all=false){
    if(!['left','center','right'].includes(value))throw Error('Неверное выравнивание.');
    const from=all||start===0?0:text.lastIndexOf('\n',start-1)+1;
    let last=start+length;if(length>0&&text[last-1]==='\n')last--;
    let to=all?text.length:text.indexOf('\n',last);if(to<0)to=text.length;
    const changed=text.slice(from,to).split('\n').map(line=>{
      const cr=line.endsWith('\r')?'\r':'';
      let content=cr?line.slice(0,-1):line;const match=content.match(alignment);if(match)content=match[2];
      return(value==='left'?content:'[align='+value+']'+content+'[/align]')+cr;
    }).join('\n');
    return checked(text.slice(0,from)+changed+text.slice(to),from,changed.length);
  }
  function shortcut(key,control,shift,alt=false){
    if(!control||alt)return null;
    return (shift?{x:'~~',e:'```'}:{b:'**',i:'*',u:'__',e:'`'})[key.toLowerCase()]||null;
  }
  const help = "Выделите текст и используйте горячие клавиши:\nCtrl+B — жирный: **текст**\nCtrl+I — курсив: *текст*\nCtrl+U — подчёркнутый: __текст__\nCtrl+Shift+X — зачёркнутый: ~~текст~~\nCtrl+E — код в строке: `текст`\nCtrl+Shift+E — блок кода: тройные обратные кавычки на отдельных строках.\n\nПовторное применение снимает оформление. Без выделения вставляются маркеры для ввода.\nВ поле ввода видна разметка, под ним — предпросмотр результата. Стили можно сочетать; внутри кода они не применяются.\n\nВыравнивание строк: Ctrl+Alt+L — влево, Ctrl+Alt+E — по центру, Ctrl+Alt+R — вправо. Добавьте Shift, чтобы применить ко всему сообщению.\nБез выделения в режиме строк изменяется строка с курсором. Перевод строки в конце выделения не затрагивает следующую строку.\nРазметка: [align=center]текст[/align] или [align=right]текст[/align].\nEnter — отправить; Shift+Enter — новая строка.\nEsc — закрыть диалог, оставив мессенджер открытым. Черновик сохраняется до закрытия страницы.\nHTML-теги не исполняются. Максимум вместе с разметкой — 4000 символов.";
  globalThis.HelperFormatting={parse,inline,toggle,align,shortcut,help};
})();
