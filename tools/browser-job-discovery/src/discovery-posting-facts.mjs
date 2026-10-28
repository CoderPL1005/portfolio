// Deliberately narrow grammar: an explicitly labelled standalone salary line,
// explicit currency, valid grouping, and an optional explicit pay period.
export function supportedPostingFacts(raw){
  const out={};
  const money=/^(?:salary|lương)\s*:\s*((?:\d{1,3}(?:[.,]\d{3})+|\d+))(?:\s*[-–]\s*((?:\d{1,3}(?:[.,]\d{3})+|\d+)))?\s*(triệu\s+)?(VND|USD|EUR)(?:\s*\/\s*(tháng|month|năm|year|giờ|hour))?\s*$/iu;
  const lines=raw.split(/\r?\n/u),matches=lines.map(line=>({line,m:line.trim().match(money)})).filter(x=>x.m);
  const labelled=lines.filter(line=>/^\s*(?:salary|lương)\s*:/iu.test(line));
  if(matches.length===1&&labelled.length===1){
    const {line,m}=matches[0],factor=m[3]?1000000:1;
    const amount=s=>s===undefined?null:Number(s.replace(/[.,]/gu,''))*factor;
    const min=amount(m[1]),max=amount(m[2])??min;
    if(Number.isSafeInteger(min)&&Number.isSafeInteger(max)&&min>0&&max>=min&&(!m[3]||m[4].toUpperCase()==='VND')){
      const period=m[5]?({tháng:'MONTHLY',month:'MONTHLY',năm:'YEARLY',year:'YEARLY',giờ:'HOURLY',hour:'HOURLY'})[m[5].toLowerCase()]:null;
      for(const [key,value] of Object.entries({salaryMinimum:min,salaryMaximum:max,salaryCurrency:m[4].toUpperCase(),salaryPeriod:period}))if(value!==null)out[key]={value,quote:line};
    }
  }
  // No timezone, relative date, publication date, or mixed sentence inference.
  const deadline=/^(?:deadline|hạn (?:nộp|ứng tuyển))\s*:\s*(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:Z|[+-]\d{2}:\d{2}))\s*$/iu;
  const dates=lines.map(line=>({line,m:line.trim().match(deadline)})).filter(x=>x.m);
  if(dates.length===1&&Number.isFinite(Date.parse(dates[0].m[1]))){
    const value=dates[0].m[1];
    // Date.parse normalizes impossible calendar days: reject those explicitly.
    const [y,m,d]=value.slice(0,10).split('-').map(Number);
    if(d<=new Date(Date.UTC(y,m,0)).getUTCDate()&&m>=1&&m<=12&&d>=1)out.expiresAt={value,quote:dates[0].line};
  }
  return out;
}
