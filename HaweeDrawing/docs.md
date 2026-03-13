Công thức tính basicX cho elbow 90 độ: 

Bước 1: xác định dữ liệu đầu vào
Tâm Elbow: Nơi 2 ống giao nhau. Gọi O(x,y,z)
Bước 2: tìm vector chỉ phương (từ tâm đi ra)
Công thức v=(Bx-Ox,By-Oy,Bz-Oz)
Ví dụ: O(1,3)-->B(4,3) ⇒ v=(4-1,3-3)=(3,0)
Bước 3: tính góc
Công thức vsum=v1+v2
Ví dụ: ∝=(1,2,3)+(4,5,6)=(5,7,9)
Góc β của elbow 90 độ: 
Vlocal=Vconnector1+Vconnector2=(-1,0,0)+(0,1,0)=(-1,1,0)
β= arctan(1-1)=135 (luôn đúng với elbow 90 độ mà chương trình đang dùng)
Bước 4: tính góc xoay (ፀ) và Basis X
ፀ=∝-β
xbasisX=cos(ፀ)
ybasisX=sin(ፀ)
zbasisX=0
Ví dụ
Có 3 điểm A(0,5), B(5,5), C(5,0)
Connector offset = 0.0675853
Ống 1 (P1) đi từ A→B
Ống 2 (P2) đi từ C→B
Giao điểm của P1 và P2 là B ⇒ Đặt elbow tại B
Hướng vector từ tâm của P1=B-C=(5-5,0-5)=(0,-5)
Hướng vector từ tâm của P2=A-B=(0-5,5-5)=(-5,0)
Vector tổng P1 và P2 là P=P1+P2=(-5,-5)
Góc ∝= arctan(-5-5)=45 NHƯNG do x<0 và y<0 nên góc phải nằm ở Góc phần tư thứ 3 nên phải là -135
Góc β= 135
Góc ፀ=∝-β=-135-135=-270
xbasisX=cos(ፀ)=0
ybasisX=sin(ፀ)=1
zbasisX=0
⇒ BasisX=(0,1,0)

Công thức tính basicX cho tee: 
Bài toán

Id
Startpoint
Endpoint
10011
(105.99999368786811, -68.6999959051609,20.99737532808399)
      
(108.07999355196954, -68.6999959051609,20.99737532808399)
10009
(108.27999355196953, -68.6999959051609, 20.99737532808399)
(110.71999339461328, -68.6999959051609, 20.99737532808399)
10093
(108.17999355196953, -65.43999610543251, 20.99737532808399)
(108.17999355196953, -68.59999590516091, 20.99737532808399)

Xác định startpoint và endpoint của ống nhánh
startpoint = (108.17999355196953, -65.43999610543251, 20.99737532808399)
endpoint = (108.17999355196953, -68.59999590516091, 20.99737532808399)
Tìm vector chỉ phương của ống (từ tâm đi ra)
Tương tự như bước tìm vector chỉ phương (từ tâm đi ra) của elbow
Vbranch=(0,-3.1599997997284,0)
Chuẩn hóa (tùy chọn)
Vbranch=(0,-1,0)
Xác định Basis X
BasisX=BasisZ * Vbranch=(0,0,1)*(vx,vy,0)
xbasisX=-vy
ybasisX=vx
⇒BasisX=(1,0,0)
