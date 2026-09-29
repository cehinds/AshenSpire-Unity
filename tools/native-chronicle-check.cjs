// Verify the visible run row without depending on the old single-line layout.
function hasRecordedClimb(labels,{classId,result,act}){
 const name=classId[0].toUpperCase()+classId.slice(1);
 return labels.some((text,index)=>text.trim()===name+' · '+result&&
  labels[index+1]?.trim().startsWith('Act '+act+','));
}
module.exports={hasRecordedClimb};
